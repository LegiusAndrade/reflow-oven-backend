namespace ReflowOven.Infrastructure.BackgroundServices;

/// <summary>
/// Data-retention windows (<c>Retention</c> section) applied by the daily sweep in
/// <see cref="SystemMonitorService"/>. On an appliance that runs for years off an SD card the
/// append-only stores would otherwise grow monotonically — and they are exactly the tables the
/// Manutenção cleanup refuses to clear. A value ≤ 0 disables that category (keep forever).
/// The Alterações audit log (<c>Changes</c>) is deliberately NOT covered: program change history
/// is kept indefinitely by design (see <c>DomainConstants.ChangeRetentionPerProgramMax</c>).
/// </summary>
public sealed class RetentionOptions
{
    public const string Section = "Retention";

    /// <summary>Age (days) after which universal operation-log rows (Log de Operação) are purged.
    /// Default one year — long enough for traceability, bounded enough for the SD card.</summary>
    public int OperationLogDays { get; set; } = 365;

    /// <summary>Age (days) a soft-deleted (cleared) notification stays in the Master's trash before it
    /// is permanently purged. The live feed is never pruned by age — only its trash.</summary>
    public int NotificationTrashDays { get; set; } = 90;
}
