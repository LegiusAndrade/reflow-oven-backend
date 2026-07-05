using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ReflowOven.Application.Abstractions;
using ReflowOven.Application.Dtos;
using ReflowOven.Application.Services;
using ReflowOven.Domain.Abstractions;
using ReflowOven.Domain.Common;
using ReflowOven.Domain.Entities;
using ReflowOven.Domain.Enums;
using ReflowOven.Infrastructure.Persistence;
using Xunit;

namespace ReflowOven.Tests;

/// <summary>
/// Login input hardening. Regression for the 500 on overlong usernames: the anonymous failure audit
/// writes the typed name into bounded columns (OperatorName varchar(40) / ObjectId varchar(64)), so a
/// 41+ char name used to blow the INSERT up into a DbUpdateException instead of the {ok,error} login
/// contract — and the throttle key grew without bound. Now an overlong name is rejected early with the
/// SAME generic message, still counted by the throttle (on a bounded key), and writes no audit row.
/// </summary>
public sealed class AuthServiceLoginTests
{
    [Fact]
    public async Task Overlong_username_fails_generically_counts_the_throttle_and_writes_no_audit_row()
    {
        await using var db = NewContext();
        var throttle = new RecordingThrottle();
        var auth = NewService(db, throttle);

        var result = await auth.LoginAsync(new LoginRequest(new string('a', DomainConstants.UserNameMaxLength + 1), "whatever"));

        Assert.False(result.Ok);
        Assert.Equal("Usuário ou senha incorretos.", result.Error); // same generic message — no length oracle
        Assert.Null(result.RetryAfterSeconds);

        // No DB write on this path: nothing to overflow (the varchar(40) OperatorName caused the old 500)
        // and no unauthenticated log-flooding either.
        Assert.Empty(await db.OperationLog.ToListAsync());

        // The failure still feeds the brute-force throttle, keyed on a bounded prefix.
        var key = Assert.Single(throttle.Failures);
        Assert.Equal(DomainConstants.UserNameMaxLength, key.Length);
    }

    [Fact]
    public async Task Max_length_username_failure_still_keeps_the_audit_row()
    {
        await using var db = NewContext();
        var throttle = new RecordingThrottle();
        var auth = NewService(db, throttle);
        var name = new string('b', DomainConstants.UserNameMaxLength); // exactly at the cap: legal input

        var result = await auth.LoginAsync(new LoginRequest(name, "wrong"));

        Assert.False(result.Ok);
        Assert.Equal("Usuário ou senha incorretos.", result.Error);
        var row = Assert.Single(await db.OperationLog.ToListAsync()); // failed attempts remain auditable
        Assert.Equal(name, row.OperatorName);
        Assert.Single(throttle.Failures);
    }

    [Fact]
    public async Task Overlong_names_share_the_bounded_throttle_bucket_with_their_prefix()
    {
        await using var db = NewContext();
        var throttle = new RecordingThrottle();
        var auth = NewService(db, throttle);
        var prefix = new string('c', DomainConstants.UserNameMaxLength);

        await auth.LoginAsync(new LoginRequest(prefix + "-tail-1", "x"));
        await auth.LoginAsync(new LoginRequest(prefix + "-a-much-longer-tail-2", "x"));

        // Both land in the same bucket, so the lockout math still converges for a long-name flood.
        Assert.Equal(2, throttle.Failures.Count);
        Assert.All(throttle.Failures, k => Assert.Equal(prefix, k));
    }

    // ---- helpers ---------------------------------------------------------

    private static ReflowDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ReflowDbContext>()
            .UseInMemoryDatabase($"auth-login-{Guid.NewGuid():N}")
            .Options;
        return new ReflowDbContext(options);
    }

    private static AuthService NewService(ReflowDbContext db, ILoginThrottle throttle)
    {
        var clock = new FixedClock();
        var current = new AnonymousUser();
        var audit = new AuditService(db, clock, current, NullLogger<AuditService>.Instance);
        return new AuthService(db, new FakeHasher(), new FakeJwt(), clock, new FakeTechnician(),
            throttle, new NoopRevocations(), new FakeEmail(), current, audit, NullLogger<AuthService>.Instance);
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 7, 3, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class AnonymousUser : ICurrentUser
    {
        public bool IsAuthenticated => false;
        public string? Id => null;
        public Guid? UserId => null;
        public string? Name => null;
        public UserType? Role => null;
        public bool IsCalibration => false;
    }

    private sealed class RecordingThrottle : ILoginThrottle
    {
        public List<string> Failures { get; } = [];
        public TimeSpan? LockRemaining(string usernameLower) => null;
        public void RecordFailure(string usernameLower) => Failures.Add(usernameLower);
        public void Reset(string usernameLower) { }
    }

    private sealed class NoopRevocations : ITokenRevocationList
    {
        public Task RevokeAsync(Guid userId, CancellationToken ct = default) => Task.CompletedTask;
        public Task RevokeAllAsync(CancellationToken ct = default) => Task.CompletedTask;
        public bool IsRevoked(Guid userId, DateTimeOffset issuedAt) => false;
    }

    private sealed class FakeHasher : IPasswordHasher
    {
        public string Hash(string password) => $"h:{password}";
        public bool Verify(string password, string hash) => hash == $"h:{password}";
    }

    private sealed class FakeJwt : IJwtTokenService
    {
        public TokenResult CreateForUser(User user) =>
            new("token", DateTimeOffset.UnixEpoch.AddDays(1), 0);
        public TokenResult CreateForCalibration() =>
            new("token", DateTimeOffset.UnixEpoch.AddDays(1), 0);
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
