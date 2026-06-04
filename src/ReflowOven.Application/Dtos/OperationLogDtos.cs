namespace ReflowOven.Application.Dtos;

/// <summary>One field of an operation-log entry's DADOS payload. <c>Before</c> is null for stateless
/// events (an execution, a login, a communication tick).</summary>
public sealed record OperationFieldDto(string Field, string? Before, string? After);

/// <summary>One row of the Log de Operação — the print's DATA · OPERADOR · TIPO · OBJETO · OBJETO ID · DADOS.
/// <c>At</c> is ISO 8601 (the client formats it). <c>OperatorName</c> is "Sistema" for automatic events.</summary>
public sealed record OperationLogEntryDto(
    Guid Id,
    DateTimeOffset At,
    string OperatorName,
    Guid? OperatorId,
    OperationCategory Category,
    OperationType Type,
    OperationObject Object,
    string? ObjectId,
    IReadOnlyList<OperationFieldDto> Data);
