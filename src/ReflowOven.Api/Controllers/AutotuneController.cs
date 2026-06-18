namespace ReflowOven.Api.Controllers;

/// <summary>
/// PID relay auto-tune: trigger/cancel the experiment, poll its live state, browse the history, and apply (or
/// dismiss) a finished tune's suggested gains. Triggering/applying is técnico-de-calibração only
/// (<see cref="AuthPolicies.CalibrationOnly"/>); reading status/history is open to any authenticated user.
/// </summary>
[ApiController]
[Route("api/autotune")]
public sealed class AutotuneController(IAutotuneManager autotune, AutotuneService service, ICurrentUser current) : ControllerBase
{
    /// <summary>Live status of the active tune, or the last finished one when idle (carries the pending "aplicar?" prompt).</summary>
    [HttpGet("status")]
    public async Task<ActionResult<AutotuneStatusDto>> Status(CancellationToken ct)
        => Ok(autotune.GetStatus() ?? await service.GetLatestAsync(ct));

    /// <summary>Paginated auto-tune history. <c>Total</c> is the full count — "quantas vezes foi feito".</summary>
    [HttpGet("history")]
    public Task<AutotuneHistoryDto> History([FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
        => service.GetHistoryAsync(page, pageSize, ct);

    [Authorize(Policy = AuthPolicies.CalibrationOnly)]
    [HttpPost("start")]
    public Task<AutotuneStatusDto> Start([FromBody] StartAutotuneRequest req, CancellationToken ct)
        => autotune.StartAsync(req.TargetTemp, current.UserId, current.Name, ct);

    [Authorize(Policy = AuthPolicies.CalibrationOnly)]
    [HttpPost("cancel")]
    public async Task<ActionResult<AutotuneStatusDto>> Cancel(CancellationToken ct) => Ok(await autotune.CancelAsync(ct));

    [Authorize(Policy = AuthPolicies.CalibrationOnly)]
    [HttpPost("{id:guid}/apply")]
    public Task<AutotuneRunDto> Apply(Guid id, CancellationToken ct) => service.ApplyAsync(id, current.UserId, current.Name, ct);

    [Authorize(Policy = AuthPolicies.CalibrationOnly)]
    [HttpPost("{id:guid}/dismiss")]
    public Task<AutotuneRunDto> Dismiss(Guid id, CancellationToken ct) => service.DismissAsync(id, ct);
}
