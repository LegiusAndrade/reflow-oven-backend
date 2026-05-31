namespace ReflowOven.Api.Controllers;

/// <summary>Self-service endpoints scoped to the current user (the JWT subject).</summary>
[ApiController]
[Route("api/me")]
public sealed class MeController(ICurrentUser current, UserService users) : ControllerBase
{
    /// <summary>The current user's UI preferences (theme + execution-chart series).</summary>
    [HttpGet("preferences")]
    public Task<UserPreferencesDto> GetPreferences(CancellationToken ct) =>
        users.GetPreferencesAsync(RequireUserId(), ct);

    [HttpPut("preferences")]
    public Task<UserPreferencesDto> UpdatePreferences([FromBody] UserPreferencesDto dto, CancellationToken ct) =>
        users.UpdatePreferencesAsync(RequireUserId(), dto, ct);

    // The hidden technician session has no User row, so it has no stored preferences.
    private Guid RequireUserId() =>
        current.UserId ?? throw new ForbiddenAppException("A sessão técnica não tem autorização para acessar ou alterar preferências de usuário.");
}
