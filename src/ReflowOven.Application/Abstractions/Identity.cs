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

    /// <summary>Synthetic full-Admin token for the hidden technician session (no User row).</summary>
    TokenResult CreateForCalibration();
}

/// <summary>Sends the password-reset email. Stubbed (logs) until real SMTP is wired.</summary>
public interface IEmailSender
{
    Task SendPasswordResetAsync(string email, string resetToken, CancellationToken ct = default);
}
