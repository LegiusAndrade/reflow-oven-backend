namespace ReflowOven.Api.Controllers;

/// <summary>
/// The Log de Operação — the universal, append-only audit trail (DATA · OPERADOR · TIPO · OBJETO · OBJETO ID
/// · DADOS) covering changes, executions, faults, communication, logins, calibration and maintenance.
/// Read-only and <b>Master-only</b> (it replaces the Master's Diagnóstico → Log tab; 403 for Admin/Regular).
/// Filters mirror the other reports plus <c>category</c>/<c>operator</c>/<c>type</c>/<c>object</c>/<c>objectId</c>.
/// </summary>
[ApiController]
[Route("api/operation-log")]
[Authorize(Policy = AuthPolicies.MasterOnly)]
public sealed class OperationLogController(OperationLogService log) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<OperationLogEntryDto>> List([FromQuery] ReportQuery query, CancellationToken ct) => log.ListAsync(query, ct);
}
