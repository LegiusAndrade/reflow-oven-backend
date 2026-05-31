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

/// <summary>Login outcome mirroring the frontend's {ok,error} contract (HTTP 200 on either branch).</summary>
public sealed record LoginResult(bool Ok, string? Error, string? Token, DateTimeOffset? ExpiresAt, SessionDto? Session)
{
    public static LoginResult Fail(string error) => new(false, error, null, null, null);
    public static LoginResult Success(string token, DateTimeOffset expiresAt, SessionDto session) =>
        new(true, null, token, expiresAt, session);
}

public sealed record ForgotPasswordRequest(string Email);

/// <summary>Always reports success to avoid account enumeration.</summary>
public sealed record OkResponse(bool Ok = true);
