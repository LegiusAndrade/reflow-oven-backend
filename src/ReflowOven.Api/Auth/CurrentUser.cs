using System.Security.Claims;

namespace ReflowOven.Api.Auth;

/// <summary>Reads the authenticated principal from the request's JWT claims (MapInboundClaims is off).</summary>
public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    public string? Id => Principal?.FindFirst("sub")?.Value ?? Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    public Guid? UserId => Guid.TryParse(Id, out var g) ? g : null;

    public string? Name => Principal?.FindFirst(ClaimTypes.Name)?.Value;

    public UserType? Role =>
        Enum.TryParse<UserType>(Principal?.FindFirst(ClaimTypes.Role)?.Value, out var r) ? r : null;

    public bool IsCalibration => Principal?.HasClaim("calibration", "true") ?? false;
}
