namespace ReflowOven.Api.Controllers;

[ApiController]
[Route("api/maintenance")]
[Authorize(Policy = AuthPolicies.AdminOnly)]
public sealed class MaintenanceController(MaintenanceService maintenance) : ControllerBase
{
    [HttpGet("overview")]
    public Task<MaintenanceOverviewDto> Overview(CancellationToken ct) => maintenance.OverviewAsync(ct);

    [HttpPost("cleanup")]
    public Task<CleanupResultDto> Cleanup([FromBody] CleanupRequest req, CancellationToken ct) => maintenance.CleanupAsync(req.Categories, ct);

    [HttpPost("factory-reset")]
    public async Task<IActionResult> FactoryReset([FromBody] FactoryResetRequest req, CancellationToken ct)
    {
        await maintenance.FactoryResetAsync(req.Confirm, ct);
        return NoContent();
    }
}
