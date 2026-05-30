namespace ReflowOven.Api.Controllers;

[ApiController]
[Route("api/runs")]
public sealed class RunsController(IRunManager runs, ICurrentUser current) : ControllerBase
{
    /// <summary>Current run status, or null when idle.</summary>
    [HttpGet("status")]
    public ActionResult<RunStatusDto> Status() => Ok(runs.GetStatus());

    [Authorize(Policy = AuthPolicies.AdminOnly)]
    [HttpPost("start")]
    public Task<RunStatusDto> Start([FromBody] StartRunRequest req, CancellationToken ct)
        => runs.StartAsync(req.ProgramId, current.UserId, current.Name, ct);

    [Authorize(Policy = AuthPolicies.AdminOnly)]
    [HttpPost("stop")]
    public async Task<ActionResult<RunStatusDto>> Stop(CancellationToken ct) => Ok(await runs.StopAsync(ct));
}
