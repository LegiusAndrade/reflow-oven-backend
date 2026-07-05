using System.Security.Claims;
using Microsoft.AspNetCore.RateLimiting;

namespace ReflowOven.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(AuthService auth, UserService users) : ControllerBase
{
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [HttpPost("login")]
    public Task<LoginResult> Login([FromBody] LoginRequest req, CancellationToken ct) => auth.LoginAsync(req, ct);

    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [HttpPost("forgot-password")]
    public Task<OkResponse> ForgotPassword([FromBody] ForgotPasswordRequest req, CancellationToken ct) => auth.ForgotPasswordAsync(req, ct);

    /// <summary>Completes the email recovery: validates the emailed token and sets the new password.</summary>
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [HttpPost("reset-password")]
    public Task<OkResponse> ResetPassword([FromBody] ResetPasswordRequest req, CancellationToken ct) => auth.ResetPasswordAsync(req, ct);

    /// <summary>Stateless logout (the client discards the JWT). Recorded on the operation log.</summary>
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        var name = User.FindFirst(ClaimTypes.Name)?.Value ?? "desconhecido";
        Guid? uid = Guid.TryParse(User.FindFirst("sub")?.Value, out var g) ? g : null;
        await auth.LogoutAsync(uid, name, ct);
        return NoContent();
    }

    /// <summary>Authenticated self-service password change (clears the forced-change flag). Revokes the
    /// old-password sessions and returns a fresh token/session so the caller stays signed in.</summary>
    [HttpPost("change-password")]
    public Task<ChangePasswordResult> ChangePassword([FromBody] ChangePasswordRequest req, CancellationToken ct) => auth.ChangePasswordAsync(req, ct);

    /// <summary>Reflects the JWT claims back as the frontend Session shape, plus the user's stored preferences.</summary>
    [HttpGet("me")]
    public async Task<ActionResult<SessionDto>> Me(CancellationToken ct)
    {
        var id = User.FindFirst("sub")?.Value ?? "";
        var name = User.FindFirst(ClaimTypes.Name)?.Value ?? "";
        var role = Enum.TryParse<UserType>(User.FindFirst(ClaimTypes.Role)?.Value, out var r) ? r : UserType.Regular;
        var calibration = User.HasClaim("calibration", "true");
        var mustChange = User.HasClaim("must_change_password", "true");
        var loginAt = long.TryParse(User.FindFirst("iat")?.Value, out var iat) ? iat * 1000 : 0;

        // Hydrate prefs from the DB (the technician session and deleted users simply have none).
        UserPreferencesDto? prefs = null;
        if (Guid.TryParse(id, out var uid))
        {
            try { prefs = await users.GetPreferencesAsync(uid, ct); }
            catch (NotFoundException) { /* token for a removed user — no prefs */ }
        }
        return new SessionDto(id, name, role, loginAt, calibration ? true : null, mustChange ? true : null,
            prefs?.Theme ?? Theme.System, prefs?.ChartSeries);
    }
}
