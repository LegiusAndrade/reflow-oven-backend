namespace ReflowOven.Application.Dtos;

/// <summary>
/// A read-only census of the database, grouped by table and broken down by the
/// status/severity/level enums. Produced by <c>SystemService.GetDatabaseAuditAsync</c>
/// for the startup audit log and the <c>/api/system/audit</c> endpoint.
/// </summary>
public sealed record DatabaseAuditDto(
    UserAuditDto Users,
    ProgramAuditDto Programs,
    ExecutionAuditDto Executions,
    ErrorAuditDto Errors,
    int Changes,
    NotificationAuditDto Notifications,
    SystemLogAuditDto SystemLog);

/// <summary>Users by status and role.</summary>
public sealed record UserAuditDto(int Total, int Active, int Inactive, int Admins);

/// <summary>Programs counted over the unfiltered set (soft-delete bypassed).</summary>
public sealed record ProgramAuditDto(int Total, int Active, int Seed, int User, int Deleted);

/// <summary>Executions split by terminal status.</summary>
public sealed record ExecutionAuditDto(int Total, int Concluido, int Falha);

/// <summary>Error-log entries split by severity.</summary>
public sealed record ErrorAuditDto(int Total, int Critico, int Alerta, int Aviso);

/// <summary>Notification feed rows, with the unread tally.</summary>
public sealed record NotificationAuditDto(int Total, int Unread);

/// <summary>System-log rows split by level.</summary>
public sealed record SystemLogAuditDto(int Total, int Info, int Aviso, int Erro);
