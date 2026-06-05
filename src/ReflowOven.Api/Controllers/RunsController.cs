namespace ReflowOven.Api.Controllers;

[ApiController]
[Route("api/runs")]
public sealed class RunsController(IRunManager runs, ICurrentUser current) : ControllerBase
{
    /// <summary>Current run status, or null when idle.</summary>
    [HttpGet("status")]
    public ActionResult<RunStatusDto> Status() => Ok(runs.GetStatus());

    [Authorize(Policy = AuthPolicies.OperatorOrAdmin)]
    [HttpPost("start")]
    public Task<RunStatusDto> Start([FromBody] StartRunRequest req, CancellationToken ct)
    {
        // The dev Master is invisible to Admin/Regular: a run it starts is attributed to "Sistema" (and no
        // user id), so it never surfaces by name in Execuções/Falhas. Real operators are passed through as-is.
        var master = current.Role == UserType.Master;
        return runs.StartAsync(req.ProgramId, master ? null : current.UserId, master ? "Sistema" : current.Name, ct);
    }

    [Authorize(Policy = AuthPolicies.OperatorOrAdmin)]
    [HttpPost("stop")]
    public async Task<ActionResult<RunStatusDto>> Stop(CancellationToken ct) => Ok(await runs.StopAsync(ct));
}
