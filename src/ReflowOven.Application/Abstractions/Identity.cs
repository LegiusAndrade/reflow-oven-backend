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

/// <summary>Per-username login throttle (in-process): after repeated failures a name is locked for a
/// cooldown, blunting brute-force and the BCrypt CPU-DoS. Keyed on the typed name (lower-cased), so a
/// lockout reveals nothing about whether the account exists. Single-device deploy → in-memory is enough.</summary>
public interface ILoginThrottle
{
    /// <summary>True while the name is in its post-failure cooldown.</summary>
    bool IsLocked(string usernameLower);

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
