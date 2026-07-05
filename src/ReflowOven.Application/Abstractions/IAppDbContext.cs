using Microsoft.EntityFrameworkCore.Storage;

namespace ReflowOven.Application.Abstractions;

/// <summary>
/// The persistence surface the Application layer depends on. Implemented by
/// <c>ReflowDbContext</c> in Infrastructure so services never reference EF provider types.
/// </summary>
public interface IAppDbContext
{
    DbSet<User> Users { get; }
    DbSet<UserActivityStat> UserActivityStats { get; }
    DbSet<PasswordResetToken> PasswordResetTokens { get; }
    DbSet<TokenRevocation> TokenRevocations { get; }

    DbSet<ReflowProgram> Programs { get; }
    DbSet<FavoriteProgram> Favorites { get; }

    DbSet<ExecutionReport> Executions { get; }
    DbSet<LogEvent> LogEvents { get; }

    DbSet<FaultType> FaultTypes { get; }
    DbSet<ErrorLogEntry> Errors { get; }

    DbSet<ChangeLogEntry> Changes { get; }
    DbSet<SystemLogEntry> SystemLog { get; }
    DbSet<OperationLogEntry> OperationLog { get; }
    DbSet<Notification> Notifications { get; }

    DbSet<Settings> Settings { get; }
    DbSet<NotificationSetting> NotificationSettings { get; }
    DbSet<RunSeriesPreference> RunSeriesPreferences { get; }

    DbSet<Calibration> Calibrations { get; }
    DbSet<DeviceInfo> DeviceInfo { get; }
    DbSet<Board> Boards { get; }
    DbSet<AutotuneRun> AutotuneRuns { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);

    /// <summary>Begins a database transaction so a multi-statement mutation built from individually
    /// auto-committing bulk operations (<c>ExecuteDelete</c>/<c>ExecuteUpdate</c>) is atomic. Returns
    /// <c>null</c> on providers without real transaction support (the in-memory test store), so callers
    /// degrade to running without one.</summary>
    Task<IDbContextTransaction?> BeginTransactionAsync(CancellationToken ct = default);

    /// <summary>Real on-disk size of the database in bytes (PostgreSQL <c>pg_database_size</c>).</summary>
    Task<long> GetDatabaseSizeBytesAsync(CancellationToken ct = default);

    /// <summary>Exact on-disk size of each table in bytes (PostgreSQL <c>pg_total_relation_size</c>: heap +
    /// indexes + TOAST), keyed by table name. Used for the per-category Manutenção breakdown.</summary>
    Task<IReadOnlyDictionary<string, long>> GetTableSizesBytesAsync(CancellationToken ct = default);
}
