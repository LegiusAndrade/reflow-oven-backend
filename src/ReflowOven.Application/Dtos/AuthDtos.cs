namespace ReflowOven.Application.Dtos;

public sealed record LoginRequest(string Username, string Password);

/// <summary>
/// The logged-in session — the shape the frontend's <c>useSession</c> expects. <c>Role</c> is
/// "Admin"/"Regular"; <c>LoginAt</c> is epoch milliseconds; <c>Calibration</c> is present (true)
/// only for the hidden technician session and omitted otherwise. <c>MustChangePassword</c> is true
/// while the user is still on the system-issued password (so the UI can prompt a change).
/// </summary>
public sealed record SessionDto(
    string Id,
    string Name,
    UserType Role,
    long LoginAt,
    bool? Calibration,
    bool? MustChangePassword = null,
    Theme Theme = Theme.System,
    RunSeriesDto? ChartSeries = null);

/// <summary>Authenticated self-service password change.</summary>
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

/// <summary>
/// Result of a successful self-service password change. Changing your own password revokes every token
/// minted under the OLD password (the security point: a stolen session dies immediately), so the response
/// carries a FRESH token + session minted after the revoke — the client swaps its stored JWT and stays
/// signed in. Shape is additive over the old <c>{ok:true}</c>: <c>Token</c>/<c>ExpiresAt</c>/<c>Session</c>
/// are new fields a client can adopt (mirrors <see cref="LoginResult"/>'s success fields).
/// </summary>
public sealed record ChangePasswordResult(bool Ok, string Token, DateTimeOffset ExpiresAt, SessionDto Session);

/// <summary>Login outcome mirroring the frontend's {ok,error} contract (HTTP 200 on either branch).
/// <c>RetryAfterSeconds</c> is set only when the attempt was throttled (lockout), so the UI can show a
/// countdown; it is omitted otherwise.</summary>
public sealed record LoginResult(bool Ok, string? Error, string? Token, DateTimeOffset? ExpiresAt, SessionDto? Session, int? RetryAfterSeconds = null)
{
    public static LoginResult Fail(string error, int? retryAfterSeconds = null) => new(false, error, null, null, null, retryAfterSeconds);
    public static LoginResult Success(string token, DateTimeOffset expiresAt, SessionDto session) =>
        new(true, null, token, expiresAt, session);
}

public sealed record ForgotPasswordRequest(string Email);

/// <summary>Completes the email recovery flow: the raw token from the email + the chosen new password.</summary>
public sealed record ResetPasswordRequest(string Token, string NewPassword);

/// <summary>Always reports success to avoid account enumeration.</summary>
public sealed record OkResponse(bool Ok = true);
