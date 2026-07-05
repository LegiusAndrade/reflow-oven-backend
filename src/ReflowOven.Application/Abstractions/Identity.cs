namespace ReflowOven.Application.Abstractions;

/// <summary>The authenticated principal for the current request (from JWT claims).</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    /// <summary>Raw subject claim: a Guid string for normal users, or "calibration" for the technician.</summary>
    string? Id { get; }

    /// <summary>Parsed user id, or null for the technician session (which has no User row).</summary>
    Guid? UserId { get; }

    string? Name { get; }
    UserType? Role { get; }

    /// <summary>True only for the hidden "calibracao" technician session.</summary>
    bool IsCalibration { get; }
}

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}

/// <summary>A minted JWT plus the timing the frontend Session needs.</summary>
public readonly record struct TokenResult(string Token, DateTimeOffset ExpiresAt, long IssuedAtUnixMs);

public interface IJwtTokenService
{
    TokenResult CreateForUser(User user);

    /// <summary>Scoped token for the hidden technician session (no User row): role <c>Tecnico</c> + the
    /// <c>calibration</c> claim, so it satisfies ONLY the CalibrationOnly policy — never Admin/Master.</summary>
    TokenResult CreateForCalibration();
}

/// <summary>
/// JWT revocation for a single-device deploy. Tokens are stateless for hours, so a mutation that narrows
/// what a user may do — deactivate, demote, delete, a self/admin/recovery password change — must actively
/// cut the user's outstanding sessions instead of waiting for expiry. Those mutations call
/// <see cref="RevokeAsync"/>; bearer/hub validation asks <see cref="IsRevoked"/> (a fast in-memory read on
/// the hot path), rejecting any token issued before the user's latest revocation mark. A fresh login after
/// the change mints a newer token, which passes. The mark is <b>persisted</b> (write-through on revoke,
/// hydrated on startup) so the appliance's own <c>Restart=always</c> can't silently un-revoke a user.
/// </summary>
public interface ITokenRevocationList
{
    /// <summary>Invalidate every token issued to this user up to now (in-memory + durable store).</summary>
    Task RevokeAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Invalidate every token issued to ANY user up to now (factory reset / bulk user cleanup).</summary>
    Task RevokeAllAsync(CancellationToken ct = default);

    /// <summary>True when a token for <paramref name="userId"/> issued at <paramref name="issuedAt"/> is
    /// no longer acceptable. Synchronous by design — it runs on every authenticated request/hub call.</summary>
    bool IsRevoked(Guid userId, DateTimeOffset issuedAt);
}

/// <summary>Per-username login throttle (in-process): after repeated failures a name is locked for a
/// cooldown, blunting brute-force and the BCrypt CPU-DoS. Keyed on the typed name (lower-cased), so a
/// lockout reveals nothing about whether the account exists. Single-device deploy → in-memory is enough.</summary>
public interface ILoginThrottle
{
    /// <summary>Remaining lockout cooldown for the name, or null if it is not currently locked.</summary>
    TimeSpan? LockRemaining(string usernameLower);

    /// <summary>Record one failed attempt; transitions to locked once the threshold is hit.</summary>
    void RecordFailure(string usernameLower);

    /// <summary>Clear the failure count + any lock (called on a successful authentication).</summary>
    void Reset(string usernameLower);
}

/// <summary>Sends transactional emails. Stub (logs) by default; real SMTP via <c>Email:Mode=Smtp</c>.</summary>
public interface IEmailSender
{
    Task SendPasswordResetAsync(string email, string resetToken, CancellationToken ct = default);

    /// <summary>Welcome email with the system-generated password and the deadline to change it.</summary>
    Task SendNewUserAsync(string email, string userName, string tempPassword, DateTimeOffset changeBy, CancellationToken ct = default);

    /// <summary>Reminder sent on login once the change deadline passed and the password is still the issued one.</summary>
    Task SendPasswordChangeReminderAsync(string email, string userName, DateTimeOffset wasDue, CancellationToken ct = default);

    /// <summary>Alerts the device admins that free disk space crossed the low threshold.</summary>
    Task SendDiskLowAsync(IEnumerable<string> adminEmails, double freePercent, double freeGB, double totalGB, CancellationToken ct = default);
}
