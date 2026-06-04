using System.Data;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using ReflowOven.Infrastructure.Persistence.Conversions;

namespace ReflowOven.Infrastructure.Persistence;

/// <summary>EF Core context for the reflow-oven backend (PostgreSQL via Npgsql). Implements <see cref="IAppDbContext"/>.</summary>
public sealed class ReflowDbContext(DbContextOptions<ReflowDbContext> options) : DbContext(options), IAppDbContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<UserActivityStat> UserActivityStats => Set<UserActivityStat>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();

    public DbSet<ReflowProgram> Programs => Set<ReflowProgram>();
    public DbSet<FavoriteProgram> Favorites => Set<FavoriteProgram>();

    public DbSet<ExecutionReport> Executions => Set<ExecutionReport>();
    public DbSet<LogEvent> LogEvents => Set<LogEvent>();

    public DbSet<FaultType> FaultTypes => Set<FaultType>();
    public DbSet<ErrorLogEntry> Errors => Set<ErrorLogEntry>();

    public DbSet<ChangeLogEntry> Changes => Set<ChangeLogEntry>();
    public DbSet<SystemLogEntry> SystemLog => Set<SystemLogEntry>();
    public DbSet<Notification> Notifications => Set<Notification>();

    public DbSet<Settings> Settings => Set<Settings>();
    public DbSet<NotificationSetting> NotificationSettings => Set<NotificationSetting>();
    public DbSet<RunSeriesPreference> RunSeriesPreferences => Set<RunSeriesPreference>();

    public DbSet<Calibration> Calibrations => Set<Calibration>();
    public DbSet<DeviceInfo> DeviceInfo => Set<DeviceInfo>();
    public DbSet<Board> Boards => Set<Board>();

    public DbSet<OperationLogEntry> OperationLog => Set<OperationLogEntry>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<User>(e =>
        {
            e.HasKey(u => u.Id);
            e.Property(u => u.Name).HasMaxLength(DomainConstants.UserNameMaxLength).IsRequired();
            e.HasIndex(u => u.Name).IsUnique();
            // DB-level guard: only letters/digits/dot (no spaces or other specials), even outside the app.
            e.ToTable(t => t.HasCheckConstraint("CK_Users_Name", $"\"Name\" ~ '{DomainConstants.UserNameDbCheck}'"));
            e.Property(u => u.Email).HasMaxLength(DomainConstants.EmailMaxLength);
            e.Property(u => u.PasswordHash).HasMaxLength(100);
            e.Property(u => u.DeletedBy).HasMaxLength(DomainConstants.UserNameMaxLength);
            e.HasQueryFilter(u => !u.IsDeleted); // soft-delete: deleted users are hidden everywhere by default
            e.HasIndex(u => u.IsDeleted);
            e.HasMany(u => u.ActivityStats).WithOne(s => s.User!).HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
            e.OwnsOne(u => u.Preferences, p => p.ToJson());
        });

        b.Entity<UserActivityStat>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.Label).HasMaxLength(64);
            e.HasIndex(s => new { s.UserId, s.Label }).IsUnique();
            e.HasQueryFilter(s => !s.User!.IsDeleted); // follow the owning user's soft-delete filter
        });

        b.Entity<PasswordResetToken>(e =>
        {
            e.HasKey(t => t.Id);
            e.Property(t => t.TokenHash).HasMaxLength(100);
            e.HasOne(t => t.User).WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(t => !t.User!.IsDeleted); // follow the owning user's soft-delete filter
        });

        b.Entity<ReflowProgram>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Id).HasMaxLength(64);
            e.Property(p => p.Name).HasMaxLength(DomainConstants.ProgramNameMaxLength).IsRequired();
            e.Property(p => p.Description).HasMaxLength(DomainConstants.ProgramDescriptionMaxLength);
            e.Property(p => p.DeletedBy).HasMaxLength(DomainConstants.UserNameMaxLength);
            e.OwnsMany(p => p.Profile, o => o.ToJson());
            e.OwnsMany(p => p.Segments, o => o.ToJson());
            e.HasQueryFilter(p => !p.IsDeleted);
            e.HasIndex(p => p.IsDeleted);
        });

        b.Entity<FavoriteProgram>(e =>
        {
            e.HasKey(f => new { f.UserId, f.ProgramId });
            e.Property(f => f.ProgramId).HasMaxLength(64);
            e.HasOne(f => f.User).WithMany().HasForeignKey(f => f.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(f => f.Program).WithMany().HasForeignKey(f => f.ProgramId).OnDelete(DeleteBehavior.Cascade);
            // Match both principals' soft-delete filters so favorites of hidden programs/users drop out too.
            e.HasQueryFilter(f => !f.Program!.IsDeleted && !f.User!.IsDeleted);
        });

        b.Entity<ExecutionReport>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.ProgramName).HasMaxLength(120);
            e.Property(x => x.UserName).HasMaxLength(40);
            e.Property(x => x.ProgramId).HasMaxLength(64);
            e.Property(x => x.PeakCurrent).HasPrecision(5, 1);
            e.Property(x => x.FaultTypeCode).HasMaxLength(10);
            e.Property(x => x.FailureReason).HasMaxLength(200);
            // LinkedErrorId is a loose reference (no FK) so the Errors↔FaultType/LogEvents delete graph stays simple.
            e.HasIndex(x => x.LinkedErrorId);
            e.OwnsMany(x => x.Points, o => o.ToJson());
            e.OwnsMany(x => x.Comparison, o => o.ToJson());
            e.OwnsOne(x => x.Trace, s =>
            {
                s.ToJson();
                s.OwnsMany(z => z.Series);
            });
            e.HasMany(x => x.Events).WithOne(v => v.ExecutionReport!).HasForeignKey(v => v.ExecutionReportId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.StartedAt);
        });

        b.Entity<LogEvent>(e =>
        {
            e.HasKey(v => v.Id);
            e.Property(v => v.Message).HasMaxLength(300);
        });

        b.Entity<FaultType>(e =>
        {
            e.HasKey(f => f.Code);
            e.Property(f => f.Code).HasMaxLength(10);
            e.Property(f => f.Message).HasMaxLength(200);
        });

        b.Entity<ErrorLogEntry>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Message).HasMaxLength(200);
            e.Property(x => x.ProgramName).HasMaxLength(120);
            e.Property(x => x.UserName).HasMaxLength(40);
            e.OwnsOne(x => x.Snapshot, s =>
            {
                s.ToJson();
                s.OwnsMany(z => z.Series);
            });
            e.HasOne(x => x.FaultType).WithMany(f => f.Errors).HasForeignKey(x => x.FaultTypeCode).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Events).WithOne(v => v.ErrorLogEntry!).HasForeignKey(v => v.ErrorLogEntryId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.At);
        });

        b.Entity<ChangeLogEntry>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Target).HasMaxLength(120);
            e.Property(x => x.UserName).HasMaxLength(40);
            e.Property(x => x.ProgramId).HasMaxLength(64);
            e.OwnsMany(x => x.Points, o => o.ToJson());
            e.HasIndex(x => x.At);
        });

        // Universal audit trail (Log de Operação). Append-only; Data is the jsonb DADOS payload.
        b.Entity<OperationLogEntry>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.OperatorName).HasMaxLength(40);
            e.Property(x => x.ObjectId).HasMaxLength(64);
            e.OwnsMany(x => x.Data, o => o.ToJson());
            e.HasIndex(x => x.At);
            e.HasIndex(x => x.Category);
            e.HasIndex(x => x.Type);
            e.HasIndex(x => x.Object);
        });

        b.Entity<SystemLogEntry>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Message).HasMaxLength(500);
            e.HasIndex(x => x.At);
        });

        b.Entity<Notification>(e =>
        {
            e.HasKey(n => n.Id);
            e.Property(n => n.Title).HasMaxLength(120);
            e.Property(n => n.Message).HasMaxLength(500);
            e.Property(n => n.DeletedBy).HasMaxLength(DomainConstants.UserNameMaxLength);
            e.HasQueryFilter(n => !n.IsDeleted); // soft-delete: cleared notifications are hidden by default
            e.HasIndex(n => n.At);
            e.HasIndex(n => n.Read);
            e.HasIndex(n => n.IsDeleted);
        });

        b.Entity<Settings>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.Id).ValueGeneratedNever();
            e.OwnsOne(s => s.Pid);
            e.OwnsOne(s => s.Oven);
            e.OwnsOne(s => s.Process);
            e.OwnsOne(s => s.Voltage);
            e.OwnsOne(s => s.Network, n =>
            {
                n.Property(x => x.Ip).HasMaxLength(DomainConstants.NetworkFieldMaxLength);
                n.Property(x => x.Mask).HasMaxLength(DomainConstants.NetworkFieldMaxLength);
                n.Property(x => x.Gateway).HasMaxLength(DomainConstants.NetworkFieldMaxLength);
                n.Property(x => x.DnsPrimary).HasMaxLength(DomainConstants.NetworkFieldMaxLength);
                n.Property(x => x.DnsSecondary).HasMaxLength(DomainConstants.NetworkFieldMaxLength);
            });
            e.HasMany(s => s.Notifications).WithOne().HasForeignKey(n => n.SettingsId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(s => s.RunSeries).WithOne().HasForeignKey(r => r.SettingsId).OnDelete(DeleteBehavior.Cascade);
            e.ToTable(t => t.HasCheckConstraint("CK_Settings_SingleRow", "\"Id\" = 1"));
        });

        b.Entity<NotificationSetting>(e =>
        {
            e.HasKey(n => n.Id);
            e.Property(n => n.Id).HasMaxLength(64).ValueGeneratedNever();
            e.Property(n => n.Alert).HasMaxLength(200);
        });

        b.Entity<RunSeriesPreference>(e =>
        {
            e.HasKey(r => new { r.SettingsId, r.Signal });
        });

        b.Entity<Calibration>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Id).ValueGeneratedNever();
            e.ToTable(t => t.HasCheckConstraint("CK_Calibration_SingleRow", "\"Id\" = 1"));
        });

        b.Entity<DeviceInfo>(e =>
        {
            e.HasKey(d => d.Id);
            e.Property(d => d.Id).ValueGeneratedNever();
            e.OwnsOne(d => d.Os);
            e.ToTable(t => t.HasCheckConstraint("CK_DeviceInfo_SingleRow", "\"Id\" = 1"));
        });

        b.Entity<Board>(e =>
        {
            e.HasKey(x => x.Role);
            e.Property(x => x.Role).ValueGeneratedNever();
            e.Property(x => x.Version).HasMaxLength(40);
            e.Property(x => x.Serial).HasMaxLength(40);
        });

        // Store every (non-JSON) enum column as its pt-BR wire text.
        foreach (var entityType in b.Model.GetEntityTypes())
        {
            if (entityType.IsMappedToJson()) continue;
            foreach (var property in entityType.GetProperties())
            {
                var clr = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
                if (!clr.IsEnum) continue;
                var converter = (ValueConverter)Activator.CreateInstance(typeof(PtBrEnumConverter<>).MakeGenericType(clr))!;
                property.SetValueConverter(converter);
            }
        }
    }

    /// <summary>Real on-disk size of the database in bytes via PostgreSQL <c>pg_database_size</c>.</summary>
    public async Task<long> GetDatabaseSizeBytesAsync(CancellationToken ct = default) =>
        await Database.SqlQuery<long>($"SELECT pg_database_size(current_database()) AS \"Value\"").SingleAsync(ct);

    /// <summary>Exact per-table size in bytes via <c>pg_total_relation_size</c>, keyed by table name.</summary>
    public async Task<IReadOnlyDictionary<string, long>> GetTableSizesBytesAsync(CancellationToken ct = default)
    {
        var sizes = new Dictionary<string, long>();
        var conn = Database.GetDbConnection();
        var wasClosed = conn.State != ConnectionState.Open;
        if (wasClosed) await conn.OpenAsync(ct);
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "SELECT c.relname, pg_total_relation_size(c.oid) " +
                "FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace " +
                "WHERE n.nspname = 'public' AND c.relkind = 'r'";
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                sizes[reader.GetString(0)] = reader.GetInt64(1);
        }
        finally
        {
            if (wasClosed) await conn.CloseAsync();
        }
        return sizes;
    }
}
