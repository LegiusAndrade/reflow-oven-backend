namespace ReflowOven.Api.Controllers;

[ApiController]
[Route("api/diagnostics")]
public sealed class DiagnosticsController(DiagnosticsService diagnostics) : ControllerBase
{
    [HttpGet("overview")]
    public Task<DiagnosticsOverviewDto> Overview([FromQuery] int rank = DomainConstants.DiagRankDefault, CancellationToken ct = default)
        => diagnostics.OverviewAsync(rank, ct);

    [HttpGet("readings")]
    public Task<SensorReadingsDto> Readings(CancellationToken ct) => diagnostics.ReadingsAsync(ct);

    /// <summary>Acknowledge and clear the board's latched protection fault (ACK_FAULT). Authenticated-by-default
    /// (NOT AdminOnly): clearing a fault is a panel safety action any logged operator may take; the
    /// acknowledgement is audited on the Log de Operação. Returns the post-ack sensor readings.</summary>
    [HttpPost("ack-fault")]
    public Task<SensorReadingsDto> AckFault(CancellationToken ct) => diagnostics.AcknowledgeFaultAsync(ct);

    [Authorize(Policy = AuthPolicies.AdminOnly)]
    [HttpPost("self-test")]
    public Task<SelfTestResultDto> SelfTest([FromBody] SelfTestRequest req, CancellationToken ct) => diagnostics.SelfTestAsync(req.Id, ct);
}

[ApiController]
[Route("api/network")]
[Authorize(Policy = AuthPolicies.AdminOnly)]
public sealed class NetworkController(DiagnosticsService diagnostics) : ControllerBase
{
    [HttpPost("ping")]
    public Task<PingResultDto> Ping([FromBody] PingRequest req, CancellationToken ct) => diagnostics.PingAsync(req.Host, req.Port, ct);
}
