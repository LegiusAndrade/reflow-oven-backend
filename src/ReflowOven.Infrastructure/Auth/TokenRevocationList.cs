using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ReflowOven.Infrastructure.Persistence;

namespace ReflowOven.Infrastructure.Auth;

/// <summary>
/// <see cref="ITokenRevocationList"/> backed by a fast in-memory map (the hot-path reader) and a durable
/// <see cref="TokenRevocation"/> table (write-through on revoke, hydrated on startup). The map holds
/// user id → the wall-clock moment of the user's latest revocation; a token is revoked when its <c>iat</c>
/// is before that mark. Marks are floored to whole seconds because the JWT <c>iat</c> has 1 s resolution.
///
/// Persistence (D2): the appliance runs <c>Restart=always</c>, so a purely in-memory list would silently
/// un-revoke every user on the next crash/restart while their 8 h tokens are still alive. Every revoke is
/// written through to the table, and <see cref="LoadFromStoreAsync"/> re-hydrates the map at boot.
///
/// Same-second boundary (D4): because <c>iat</c> carries only whole seconds, this list cannot distinguish
/// two tokens minted in the <b>same second</b> as a revoke — the comparison is <c>iat &lt; mark</c> with
/// both floored to the second, so a token whose <c>iat</c> equals the revoke second is NOT revoked. This is
/// a ≤1 s fail-open window inherent to JWT <c>iat</c> resolution, not a designed feature; it is also what
/// lets a legitimate re-login (minted right after the revoke) pass. Closing it exactly would require a
/// per-token identifier (a <c>jti</c> allow/deny set) — possible future work, deliberately out of scope here.
/// </summary>
public sealed class TokenRevocationList(
    IServiceScopeFactory scopeFactory,
    IOptions<JwtOptions> jwtOptions,
    IClock clock,
    ILogger<TokenRevocationList> logger) : ITokenRevocationList
{
    /// <summary>Bearer validation allows a 30 s ClockSkew (see Program.cs); keep entries at least that
    /// much longer so a token that could still validate is never forgotten early.</summary>
    private static readonly TimeSpan ValidationSkew = TimeSpan.FromSeconds(30);

    /// <summary>Sentinel subject for the global (revoke-everyone) mark. <c>Guid.NewGuid</c> never yields
    /// Empty, so it can never collide with a real user id.</summary>
    private static readonly Guid AllSubject = Guid.Empty;

    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _revokedAt = new();

    /// <summary>UtcTicks of the global (revoke-everyone) mark; 0 = never. Written via Interlocked.</summary>
    private long _allRevokedAtTicks;

    private TimeSpan MaxTokenLifetime => TimeSpan.FromMinutes(jwtOptions.Value.ExpiryMinutes) + ValidationSkew;

    public async Task RevokeAsync(Guid userId, CancellationToken ct = default)
    {
        // ct governs only the instant, in-memory decision below; the durable write runs uncancellable
        // (see PersistAsync) so a client abort can never drop a security mark. In-memory first so
        // enforcement is guaranteed for this process even if the DB write later fails.
        _ = ct;
        var mark = FloorToSecond(clock.UtcNow);
        _revokedAt.AddOrUpdate(userId, mark, (_, prev) => prev > mark ? prev : mark);
        Prune();
        await PersistAsync(userId, mark);
    }

    public async Task RevokeAllAsync(CancellationToken ct = default)
    {
        _ = ct; // see RevokeAsync — the durable write is deliberately not request-cancellable
        var mark = FloorToSecond(clock.UtcNow);
        AdvanceGlobal(mark.UtcTicks);
        await PersistAsync(AllSubject, mark);
    }

    public bool IsRevoked(Guid userId, DateTimeOffset issuedAt)
    {
        var all = Interlocked.Read(ref _allRevokedAtTicks);
        if (all != 0 && issuedAt.UtcTicks < all) return true;
        return _revokedAt.TryGetValue(userId, out var mark) && issuedAt < mark;
    }

    /// <summary>Re-hydrate the in-memory marks from the durable table. Called once at startup (before the
    /// app serves traffic) so a restart doesn't lose revocations; safe to call again (idempotent — it only
    /// advances marks forward). Rows already older than any live token are SKIPPED (not loaded); they are
    /// left in the table (harmless — they can no longer block a live token) until a later revoke of the
    /// same subject overwrites them.</summary>
    public async Task LoadFromStoreAsync(CancellationToken ct = default)
    {
        var horizon = clock.UtcNow - MaxTokenLifetime;
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var rows = await db.TokenRevocations.AsNoTracking().ToListAsync(ct);
        var loaded = 0;
        foreach (var row in rows)
        {
            if (row.Subject == AllSubject)
                AdvanceGlobal(row.RevokedAt.UtcTicks);
            else if (row.RevokedAt >= horizon) // skip marks that can no longer block any live token
            {
                _revokedAt.AddOrUpdate(row.Subject, row.RevokedAt, (_, prev) => prev > row.RevokedAt ? prev : row.RevokedAt);
                loaded++;
            }
        }
        logger.LogInformation("Revogação de tokens hidratada: {Users} usuário(s) + marca global {Global}.",
            loaded, Interlocked.Read(ref _allRevokedAtTicks) != 0 ? "presente" : "ausente");
    }

    /// <summary>
    /// Write-through the subject's mark durably. Runs with <see cref="CancellationToken.None"/> on purpose:
    /// a revocation is a security decision that MUST land regardless of the originating request's lifecycle,
    /// so a client abort (the request token) can never cancel — and silently lose — the write (the P1 hole).
    /// The upsert is atomic and never regresses: on Postgres a single <c>INSERT … ON CONFLICT DO UPDATE …
    /// GREATEST</c> can't PK-violate on two racing first-revokes, and an out-of-order older mark can't
    /// overwrite a newer one. A genuine store failure is logged at Error (never swallowed as success); the
    /// in-memory mark still enforces the revocation for this process. Residual: a process crash in the small
    /// window between the caller's commit and this write would lose the durable mark — inherent to a
    /// post-commit write; the request-cancellation vector (the reported exploit) is fully closed by None.
    /// </summary>
    private async Task PersistAsync(Guid subject, DateTimeOffset mark)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ReflowDbContext>();
            if (db.Database.IsRelational())
            {
                // Atomic, concurrency-safe, never-regressing upsert. Postgres is the only relational provider.
                await db.Database.ExecuteSqlAsync(
                    $"""
                    INSERT INTO "TokenRevocations" ("Subject", "RevokedAt")
                    VALUES ({subject}, {mark})
                    ON CONFLICT ("Subject") DO UPDATE
                        SET "RevokedAt" = GREATEST(excluded."RevokedAt", "TokenRevocations"."RevokedAt")
                    """,
                    CancellationToken.None);
            }
            else
            {
                // InMemory (tests): no raw SQL / ON CONFLICT — a guarded upsert that likewise never regresses.
                var row = await db.TokenRevocations.FirstOrDefaultAsync(r => r.Subject == subject, CancellationToken.None);
                if (row is null)
                    db.TokenRevocations.Add(new TokenRevocation { Subject = subject, RevokedAt = mark });
                else if (row.RevokedAt < mark)
                    row.RevokedAt = mark;
                else
                    return; // nothing to advance
                await db.SaveChangesAsync(CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao persistir a revogação de token do sujeito {Subject} (marca em memória mantida).", subject);
        }
    }

    /// <summary>Move the global mark forward to <paramref name="markTicks"/> (never backwards).</summary>
    private void AdvanceGlobal(long markTicks)
    {
        long prev;
        do
        {
            prev = Interlocked.Read(ref _allRevokedAtTicks);
            if (prev >= markTicks) return;
        }
        while (Interlocked.CompareExchange(ref _allRevokedAtTicks, markTicks, prev) != prev);
    }

    /// <summary>JWT iat carries whole seconds — compare marks at the same resolution.</summary>
    private static DateTimeOffset FloorToSecond(DateTimeOffset t) =>
        DateTimeOffset.FromUnixTimeSeconds(t.ToUnixTimeSeconds());

    /// <summary>Drop marks older than any token they could still block (revocations are rare; O(n) is fine).</summary>
    private void Prune()
    {
        var horizon = clock.UtcNow - MaxTokenLifetime;
        foreach (var (userId, mark) in _revokedAt)
            if (mark < horizon)
                ((ICollection<KeyValuePair<Guid, DateTimeOffset>>)_revokedAt).Remove(new(userId, mark));
    }
}
