using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ReflowOven.Application.Abstractions;
using ReflowOven.Application.Dtos;
using ReflowOven.Application.Services;
using ReflowOven.Domain.Abstractions;
using ReflowOven.Domain.Entities;
using ReflowOven.Domain.Enums;
using ReflowOven.Infrastructure.Auth;
using ReflowOven.Infrastructure.Persistence;
using Xunit;

namespace ReflowOven.Tests;

/// <summary>
/// JWT revocation (single-device): deactivating/demoting/deleting a user, or a self/admin/recovery password
/// change, invalidates the user's outstanding tokens immediately instead of letting them ride out the 8 h
/// expiry. Covers the <see cref="TokenRevocationList"/> mark semantics (iat-based, second resolution, re-login
/// passes), the durable watermark surviving a re-hydration (D2), and the <see cref="UserService"/> /
/// <see cref="AuthService"/> mutations that feed it (including self-service change-password, D1).
/// </summary>
public sealed class TokenRevocationTests
{
    [Fact]
    public async Task Revoke_blocks_tokens_issued_before_the_mark_and_allows_a_fresh_login()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 7, 3, 12, 0, 0, TimeSpan.Zero));
        await using var sp = BuildProvider();
        var list = NewList(sp, clock);
        var userId = Guid.NewGuid();

        var oldToken = clock.UtcNow.AddHours(-2); // minted two hours ago
        Assert.False(list.IsRevoked(userId, oldToken)); // nothing revoked yet

        clock.Advance(TimeSpan.FromMinutes(5));
        await list.RevokeAsync(userId);

        Assert.True(list.IsRevoked(userId, oldToken));            // the outstanding session dies
        Assert.False(list.IsRevoked(Guid.NewGuid(), oldToken));   // other users are untouched

        // A re-login AFTER the revocation mints a newer iat — that token must pass.
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.False(list.IsRevoked(userId, clock.UtcNow));
    }

    [Fact]
    public async Task Revoke_marks_have_second_resolution_so_a_same_second_relogin_passes()
    {
        // JWT iat carries whole seconds: a token minted in the SAME second as the revocation must pass
        // (it can only be the fresh post-change login), while anything from an earlier second fails.
        var clock = new TestClock(new DateTimeOffset(2026, 7, 3, 12, 0, 0, 500, TimeSpan.Zero));
        await using var sp = BuildProvider();
        var list = NewList(sp, clock);
        var userId = Guid.NewGuid();

        await list.RevokeAsync(userId); // mark floors to 12:00:00
        var sameSecondIat = new DateTimeOffset(2026, 7, 3, 12, 0, 0, TimeSpan.Zero);
        var previousSecondIat = sameSecondIat.AddSeconds(-1);

        Assert.False(list.IsRevoked(userId, sameSecondIat));
        Assert.True(list.IsRevoked(userId, previousSecondIat));
    }

    [Fact]
    public async Task RevokeAll_blocks_every_user_until_they_relogin()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 7, 3, 12, 0, 0, TimeSpan.Zero));
        await using var sp = BuildProvider();
        var list = NewList(sp, clock);
        var (a, b) = (Guid.NewGuid(), Guid.NewGuid());
        var beforeReset = clock.UtcNow.AddMinutes(-30);

        clock.Advance(TimeSpan.FromMinutes(1));
        await list.RevokeAllAsync(); // e.g. a factory reset

        Assert.True(list.IsRevoked(a, beforeReset));
        Assert.True(list.IsRevoked(b, beforeReset));
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.False(list.IsRevoked(a, clock.UtcNow)); // fresh post-reset login is fine
    }

    [Fact]
    public async Task A_persisted_watermark_survives_a_restart_via_rehydration()
    {
        // D2: the appliance runs Restart=always, so an in-memory-only list would un-revoke a user on the
        // next restart. Prove the mark round-trips: revoke through list1 (persists), then a FRESH list2
        // (simulating the restarted process) hydrates from the shared store and still rejects the old token.
        var clock = new TestClock(new DateTimeOffset(2026, 7, 3, 12, 0, 0, TimeSpan.Zero));
        await using var sp = BuildProvider(); // one provider = one shared in-memory DB across both lists
        var userId = Guid.NewGuid();
        var oldToken = clock.UtcNow.AddHours(-1);

        var list1 = NewList(sp, clock);
        await list1.RevokeAsync(userId);

        var list2 = NewList(sp, clock); // brand-new in-memory maps, same durable store
        Assert.False(list2.IsRevoked(userId, oldToken)); // not yet hydrated → would wrongly allow
        await list2.LoadFromStoreAsync();
        Assert.True(list2.IsRevoked(userId, oldToken));  // hydrated → the revocation survived the "restart"

        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.False(list2.IsRevoked(userId, clock.UtcNow)); // a post-revoke login still passes
    }

    [Fact]
    public async Task A_persisted_RevokeAll_survives_rehydration()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 7, 3, 12, 0, 0, TimeSpan.Zero));
        await using var sp = BuildProvider();
        var userId = Guid.NewGuid();
        var oldToken = clock.UtcNow.AddMinutes(-10);

        var list1 = NewList(sp, clock);
        await list1.RevokeAllAsync();

        var list2 = NewList(sp, clock);
        await list2.LoadFromStoreAsync();
        Assert.True(list2.IsRevoked(userId, oldToken)); // global mark rehydrated
    }

    [Fact]
    public async Task Revoke_with_an_already_cancelled_token_still_persists_and_survives_rehydration()
    {
        // D-1: a revocation is a security decision — an aborted request (cancelled ct) must NOT drop the
        // durable write, or the mark would be lost on the next restart (the P1 hole). Persist runs uncancellable.
        var clock = new TestClock(new DateTimeOffset(2026, 7, 3, 12, 0, 0, TimeSpan.Zero));
        await using var sp = BuildProvider();
        var userId = Guid.NewGuid();
        var oldToken = clock.UtcNow.AddHours(-1);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var list1 = NewList(sp, clock);
        await list1.RevokeAsync(userId, cts.Token); // already-cancelled ct passed by the caller

        var list2 = NewList(sp, clock); // simulated restart: fresh in-memory maps, same durable store
        await list2.LoadFromStoreAsync();
        Assert.True(list2.IsRevoked(userId, oldToken)); // persisted despite the cancelled ct
    }

    [Fact]
    public async Task Persisted_mark_never_regresses_to_an_earlier_out_of_order_revoke()
    {
        // D-3: an older mark arriving after a newer one (clock step / out-of-order) must not overwrite the
        // stored newer mark. Verified here on the guarded-upsert path; the Postgres ON CONFLICT … GREATEST
        // path is exercised in the live Pi smoke.
        var clock = new TestClock(new DateTimeOffset(2026, 7, 3, 12, 0, 10, TimeSpan.Zero));
        await using var sp = BuildProvider();
        var userId = Guid.NewGuid();
        var list = NewList(sp, clock);

        await list.RevokeAsync(userId);                 // persist mark = 12:00:10
        clock.Advance(TimeSpan.FromSeconds(-5));         // clock steps back to 12:00:05
        await list.RevokeAsync(userId);                 // an out-of-order older revoke must NOT regress the store

        var fresh = NewList(sp, clock);                 // hydrate only from the durable store
        await fresh.LoadFromStoreAsync();
        Assert.True(fresh.IsRevoked(userId, new DateTimeOffset(2026, 7, 3, 12, 0, 7, TimeSpan.Zero)));   // < 12:00:10 → revoked
        Assert.False(fresh.IsRevoked(userId, new DateTimeOffset(2026, 7, 3, 12, 0, 12, TimeSpan.Zero))); // > 12:00:10 → not
    }

    [Fact]
    public async Task Self_service_change_password_revokes_old_sessions_and_returns_a_fresh_valid_token()
    {
        // D1: the most security-relevant password path. Changing your own password must kill the tokens
        // minted under the old password AND hand back a fresh one so the caller isn't logged out.
        var clock = new TestClock(new DateTimeOffset(2026, 7, 3, 12, 0, 0, TimeSpan.Zero));
        await using var sp = BuildProvider();
        var list = NewList(sp, clock);

        var db = sp.GetRequiredService<ReflowDbContext>();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Name = "operador.self",
            Email = "operador.self@x.com",
            PasswordHash = "h:senha-antiga",
            Type = UserType.Regular,
            Status = UserStatus.Ativo,
            CreatedAt = clock.UtcNow.AddDays(-1),
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var oldTokenIat = clock.UtcNow.AddHours(-1); // a session minted an hour ago, before the change
        var auth = NewAuthService(sp, clock, user.Id, list);

        var result = await auth.ChangePasswordAsync(new ChangePasswordRequest("senha-antiga", "senha-nova-123"));

        Assert.True(result.Ok);
        Assert.False(string.IsNullOrEmpty(result.Token));       // a fresh token came back…
        Assert.Equal(user.Id.ToString(), result.Session.Id);    // …for the same user (kept signed in)

        // The old session is now revoked; a token minted at the change second (the returned one) still passes.
        Assert.True(list.IsRevoked(user.Id, oldTokenIat));
        var changeSecond = DateTimeOffset.FromUnixTimeSeconds(clock.UtcNow.ToUnixTimeSeconds());
        Assert.False(list.IsRevoked(user.Id, changeSecond));
    }

    [Fact]
    public async Task Expired_provisional_password_rotation_revokes_old_sessions()
    {
        // D-5: rewriting the hash when an expired provisional password is rotated must cut old sessions,
        // consistent with every other password-set path (self/admin/recovery).
        var clock = new TestClock(new DateTimeOffset(2026, 7, 3, 12, 0, 0, TimeSpan.Zero));
        await using var sp = BuildProvider();
        var db = sp.GetRequiredService<ReflowDbContext>();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Name = "novato.teste",
            Email = "novato.teste@x.com",
            PasswordHash = "h:prov-pass",
            Type = UserType.Regular,
            Status = UserStatus.Ativo,
            CreatedAt = clock.UtcNow.AddDays(-10),
            MustChangePassword = true,
            PasswordIssuedAt = clock.UtcNow.AddDays(-10), // deadline (issued + 7d) is in the past
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var revocations = new RecordingRevocations();
        var auth = NewAuthService(sp, clock, user.Id, revocations);

        var result = await auth.LoginAsync(new LoginRequest("novato.teste", "prov-pass"));

        Assert.False(result.Ok);                       // login refused — must use the freshly emailed password
        Assert.Contains("provisória", result.Error);   // the expired-provisional message
        Assert.Contains(user.Id, revocations.Revoked); // …and old sessions were cut
    }

    [Fact]
    public async Task Deactivating_a_user_revokes_its_outstanding_tokens()
    {
        await using var db = NewContext();
        var revocations = new RecordingRevocations();
        var svc = NewUserService(db, revocations);
        var user = await SeedRegularAsync(db, "operador.teste");

        await svc.UpdateAsync(user.Id, new UpdateUserRequest(user.Email, UserType.Regular, UserStatus.Inativo));

        Assert.Contains(user.Id, revocations.Revoked);
    }

    [Fact]
    public async Task Changing_a_users_role_revokes_but_an_email_only_edit_does_not()
    {
        await using var db = NewContext();
        var revocations = new RecordingRevocations();
        var svc = NewUserService(db, revocations);
        var user = await SeedRegularAsync(db, "promovido.teste");

        // Email-only edit: the bearer's authority is unchanged — no revocation, no forced re-login.
        await svc.UpdateAsync(user.Id, new UpdateUserRequest("novo@x.com", UserType.Regular, UserStatus.Ativo));
        Assert.Empty(revocations.Revoked);

        // Role change: the role claim inside the outstanding JWT is now stale — revoke.
        await svc.UpdateAsync(user.Id, new UpdateUserRequest("novo@x.com", UserType.Admin, UserStatus.Ativo));
        Assert.Contains(user.Id, revocations.Revoked);
    }

    [Fact]
    public async Task Deleting_a_user_revokes_its_outstanding_tokens()
    {
        await using var db = NewContext();
        var revocations = new RecordingRevocations();
        var svc = NewUserService(db, revocations);
        var user = await SeedRegularAsync(db, "removido.teste");

        await svc.DeleteAsync(user.Id);

        Assert.Contains(user.Id, revocations.Revoked);
    }

    // ---- helpers ---------------------------------------------------------

    /// <summary>A provider with a single shared in-memory DB, so two <see cref="TokenRevocationList"/>
    /// instances built from it write/read the same durable store (used to simulate a restart).</summary>
    private static ServiceProvider BuildProvider()
    {
        var dbName = $"revoke-store-{Guid.NewGuid():N}";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ReflowDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddScoped<IAppDbContext>(s => s.GetRequiredService<ReflowDbContext>());
        return services.BuildServiceProvider();
    }

    private static TokenRevocationList NewList(IServiceProvider sp, IClock clock) =>
        new(sp.GetRequiredService<IServiceScopeFactory>(), Options.Create(new JwtOptions()), clock,
            NullLogger<TokenRevocationList>.Instance);

    private static AuthService NewAuthService(IServiceProvider sp, IClock clock, Guid callerId, ITokenRevocationList revocations)
    {
        var db = sp.GetRequiredService<ReflowDbContext>();
        var current = new FixedUser(callerId);
        var audit = new AuditService(db, clock, current, NullLogger<AuditService>.Instance);
        return new AuthService(db, new FakeHasher(), new JwtTokenService(Options.Create(new JwtOptions()), clock),
            clock, new FakeTechnician(), new NoopThrottle(), revocations, new FakeEmail(), current, audit,
            NullLogger<AuthService>.Instance);
    }

    private static ReflowDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ReflowDbContext>()
            .UseInMemoryDatabase($"revoke-{Guid.NewGuid():N}")
            .Options;
        return new ReflowDbContext(options);
    }

    private static UserService NewUserService(ReflowDbContext db, ITokenRevocationList revocations)
    {
        var clock = new TestClock(new DateTimeOffset(2026, 7, 3, 12, 0, 0, TimeSpan.Zero));
        var current = new AdminUser();
        var audit = new AuditService(db, clock, current, NullLogger<AuditService>.Instance);
        return new UserService(db, new FakeHasher(), clock, audit, current,
            new FakeEmail(), revocations, NullLogger<UserService>.Instance);
    }

    /// <summary>A plain Regular row inserted directly (skips CreateAsync's BCrypt + welcome email).</summary>
    private static async Task<User> SeedRegularAsync(ReflowDbContext db, string name)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Name = name,
            Email = $"{name}@x.com",
            PasswordHash = "h",
            Type = UserType.Regular,
            Status = UserStatus.Ativo,
            CreatedAt = new DateTimeOffset(2026, 7, 1, 8, 0, 0, TimeSpan.Zero),
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private sealed class TestClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; private set; } = now;
        public void Advance(TimeSpan by) => UtcNow += by;
    }

    private sealed class RecordingRevocations : ITokenRevocationList
    {
        public List<Guid> Revoked { get; } = [];
        public bool AllRevoked { get; private set; }
        public Task RevokeAsync(Guid userId, CancellationToken ct = default) { Revoked.Add(userId); return Task.CompletedTask; }
        public Task RevokeAllAsync(CancellationToken ct = default) { AllRevoked = true; return Task.CompletedTask; }
        public bool IsRevoked(Guid userId, DateTimeOffset issuedAt) => false;
    }

    private sealed class AdminUser : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? Id => UserId.ToString();
        public Guid? UserId { get; } = Guid.NewGuid();
        public string? Name => "admin.teste";
        public UserType? Role => UserType.Admin;
        public bool IsCalibration => false;
    }

    private sealed class FixedUser(Guid id) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? Id => UserId.ToString();
        public Guid? UserId { get; } = id;
        public string? Name => "operador.self";
        public UserType? Role => UserType.Regular;
        public bool IsCalibration => false;
    }

    private sealed class FakeHasher : IPasswordHasher
    {
        public string Hash(string password) => $"h:{password}";
        public bool Verify(string password, string hash) => hash == $"h:{password}";
    }

    private sealed class NoopThrottle : ILoginThrottle
    {
        public TimeSpan? LockRemaining(string usernameLower) => null;
        public void RecordFailure(string usernameLower) { }
        public void Reset(string usernameLower) { }
    }

    private sealed class FakeTechnician : ITechnicianCredentials
    {
        public string Username => "calibracao";
        public bool Verify(string password) => false;
    }

    private sealed class FakeEmail : IEmailSender
    {
        public Task SendPasswordResetAsync(string email, string resetToken, CancellationToken ct = default) => Task.CompletedTask;
        public Task SendNewUserAsync(string email, string userName, string tempPassword, DateTimeOffset changeBy, CancellationToken ct = default) => Task.CompletedTask;
        public Task SendPasswordChangeReminderAsync(string email, string userName, DateTimeOffset wasDue, CancellationToken ct = default) => Task.CompletedTask;
        public Task SendDiskLowAsync(IEnumerable<string> adminEmails, double freePercent, double freeGB, double totalGB, CancellationToken ct = default) => Task.CompletedTask;
    }
}
