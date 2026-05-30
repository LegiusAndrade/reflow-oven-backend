namespace ReflowOven.Api.Controllers;

// Read-only Relatórios endpoints. Available to any authenticated user (the fallback policy).

[ApiController]
[Route("api/executions")]
public sealed class ExecutionsController(ReportService reports) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<ExecutionSummaryDto>> List([FromQuery] ReportQuery query, CancellationToken ct) => reports.ExecutionsAsync(query, ct);

    [HttpGet("{id:guid}")]
    public Task<ExecutionDetailDto> Get(Guid id, CancellationToken ct) => reports.ExecutionAsync(id, ct);
}

[ApiController]
[Route("api/changes")]
public sealed class ChangesController(ReportService reports) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<ChangeSummaryDto>> List([FromQuery] ReportQuery query, CancellationToken ct) => reports.ChangesAsync(query, ct);

    [HttpGet("{id:guid}")]
    public Task<ChangeDetailDto> Get(Guid id, CancellationToken ct) => reports.ChangeAsync(id, ct);
}

[ApiController]
[Route("api/errors")]
public sealed class ErrorsController(ReportService reports) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<ErrorSummaryDto>> List([FromQuery] ReportQuery query, CancellationToken ct) => reports.ErrorsAsync(query, ct);

    [HttpGet("{id:guid}")]
    public Task<ErrorDetailDto> Get(Guid id, CancellationToken ct) => reports.ErrorAsync(id, ct);
}

[ApiController]
[Route("api/system-log")]
public sealed class SystemLogController(ReportService reports) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<SystemLogDto>> List([FromQuery] ReportQuery query, CancellationToken ct) => reports.SystemLogAsync(query, ct);
}

[ApiController]
[Route("api/fault-types")]
public sealed class FaultTypesController(ReportService reports) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<FaultTypeDto>> List(CancellationToken ct) => reports.FaultTypesAsync(ct);
}
