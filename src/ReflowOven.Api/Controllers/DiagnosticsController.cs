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
