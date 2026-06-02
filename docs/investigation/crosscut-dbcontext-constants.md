# crosscut-dbcontext-constants

I now have everything required. Producing the report.

## Files

### /home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Infrastructure/Persistence/ReflowDbContext.cs
The full `OnModelCreating`, the enum-converter loop, and the DbSet list / size helpers:

```csharp
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
            e.HasMany(u => u.ActivityStats).WithOne(s => s.User!).HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
            e.OwnsOne(u => u.Preferences, p => p.ToJson());
        });

        b.Entity<UserActivityStat>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.Label).HasMaxLength(64);
            e.HasIndex(s => new { s.UserId, s.Label }).IsUnique();
        });

        b.Entity<PasswordResetToken>(e =>
        {
            e.HasKey(t => t.Id);
            e.Property(t => t.TokenHash).HasMaxLength(100);
            e.HasOne(t => t.User).WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ReflowProgram>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Id).HasMaxLength(64);
            e.Property(p => p.Name).HasMaxLength(DomainConstants.ProgramNameMaxLength).IsRequired();
            e.Property(p => p.Description).HasMaxLength(DomainConstants.ProgramDescriptionMaxLength);
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
            // Match the principal's soft-delete filter so favorites of hidden programs drop out too.
            e.HasQueryFilter(f => !f.Program!.IsDeleted);
        });

        b.Entity<ExecutionReport>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.ProgramName).HasMaxLength(120);
            e.Property(x => x.UserName).HasMaxLength(40);
            e.Property(x => x.ProgramId).HasMaxLength(64);
            e.Property(x => x.PeakCurrent).HasPrecision(5, 1);
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
            e.HasIndex(n => n.At);
            e.HasIndex(n => n.Read);
        });
        // ...Settings / NotificationSetting / RunSeriesPreference / Calibration / DeviceInfo / Board omitted...

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
```

The Settings/Calibration/DeviceInfo singletons (lines 161-207) carry the only check constraints in addition to `CK_Users_Name`:
```csharp
e.ToTable(t => t.HasCheckConstraint("CK_Settings_SingleRow", "\"Id\" = 1"));
e.ToTable(t => t.HasCheckConstraint("CK_Calibration_SingleRow", "\"Id\" = 1"));
e.ToTable(t => t.HasCheckConstraint("CK_DeviceInfo_SingleRow", "\"Id\" = 1"));
```
There is NO check constraint on any enum column. Settings owns its value objects with `OwnsOne` (Pid/Oven/Process/Voltage/Network — kept as columns, NOT `.ToJson()`); `User.Preferences`, `ReflowProgram.Profile/Segments`, `ExecutionReport.Points/Comparison/Trace`, `ErrorLogEntry.Snapshot`, `ChangeLogEntry.Points` are the jsonb ones.

### /home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Application/Abstractions/IAppDbContext.cs
DbSet list mirrors the context exactly (Users, UserActivityStats, PasswordResetTokens, Programs, Favorites, Executions, LogEvents, FaultTypes, Errors, Changes, SystemLog, Notifications, Settings, NotificationSettings, RunSeriesPreferences, Calibrations, DeviceInfo, Boards) plus:
```csharp
Task<int> SaveChangesAsync(CancellationToken ct = default);
Task<long> GetDatabaseSizeBytesAsync(CancellationToken ct = default);
Task<IReadOnlyDictionary<string, long>> GetTableSizesBytesAsync(CancellationToken ct = default);
```
Note: a new entity needs a `DbSet` added to BOTH `IAppDbContext` and `ReflowDbContext`. A new scalar property on an existing entity does NOT touch this file.

### /home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Domain/Common/DomainConstants.cs
Full file (116 lines, all `public const`). Mirrors `../reflow-oven-front/src/lib/limits.ts`. Key constants by group:
- Programs/profiles: `ProgramNameMaxLength=40`, `ProgramDescriptionMaxLength=120`, `ProfileMaxPoints=100`, `PointTempMin=0`, `PointTempMax=500`, `PointDurationMin=0`, `PointDurationMax=3600`, `StartTemp=25`.
- Config: `PidMin=0`, `PidMax=1000`, `ConfigTempMin=0`, `ConfigTempMax=500`, `ConfigFanRpmMin=0`, `ConfigFanRpmMax=10000`, `ConfigExtraTimeMin=0`, `ConfigExtraTimeMax=3600`, `ConfigVoltageMin=0`, `ConfigVoltageMax=300`, `NetworkFieldMaxLength=15`, `WifiSsidMaxLength=32`, `WifiPasswordMaxLength=63`.
- Users: `UserNameMinLength=3`, `UserNameMaxLength=40`, `EmailMaxLength=254`, `UserNameRegex=@"^[\p{L}\p{N}.]+$"`, `UserNameDbCheck="^[[:alnum:].]+$"`, `PasswordMinLength=8`, `PasswordMaxLength=72`, `PasswordChangeWithinDays=7`, `GeneratedPasswordLength=14`, `EmailRegex=@"^[^\s@]+@[^\s@]+\.[^\s@]+$"`.
- Paging: `ReportPageSizeMax=200`, `ProgramPageSizeMax=100`, `NotificationFeedMax=200`, `DiskLowFreePercent=10`, `ChangeRetentionPerProgramMax=10`.
- Diagnostics: `DiagRankMin=3`, `DiagRankMax=10`, `DiagRankDefault=5`.
- Run: `RunMeasuredMaxPoints=600`, `RunTickMs=1000`.
- Reports/snapshot: `SnapshotSamples=40`.
- Calibration: `CalibThermoOffsetMin=-20`, `CalibThermoOffsetMax=20`, `CalibCurrentOffsetMin=-5`, `CalibCurrentOffsetMax=5`, `CalibGainMin=50`, `CalibGainMax=150`, `CalibPwmMin=0`, `CalibPwmMax=100`.

### /home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Infrastructure/Persistence/Conversions/PtBrEnumConverter.cs
```csharp
public sealed class PtBrEnumConverter<TEnum>() : ValueConverter<TEnum, string>(
    v => EnumWire.ToWire(v),
    s => EnumWire.FromWire<TEnum>(s))
    where TEnum : struct, Enum;
```
It defers to `EnumWire` (/home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Domain/Common/EnumWire.cs), which reads `[JsonStringEnumMemberName]` via reflection (cached), falling back to the C# member name when the attribute is absent:
```csharp
var wire = f.GetCustomAttribute<JsonStringEnumMemberNameAttribute>()?.Name ?? f.Name;
```
Stores the enum as plain `string` (column type `text`, no length/check). Adding a new enum member is automatically picked up by reflection at runtime — no mapping change needed.

### /home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Infrastructure/Persistence/ReflowDbContextFactory.cs
```csharp
public sealed class ReflowDbContextFactory : IDesignTimeDbContextFactory<ReflowDbContext>
{
    public ReflowDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
                 ?? "Host=localhost;Port=5432;Database=reflowoven;Username=reflow;Password=reflow";
        var options = new DbContextOptionsBuilder<ReflowDbContext>().UseNpgsql(cs).Options;
        return new ReflowDbContext(options);
    }
}
```
So `dotnet ef migrations add` works without a running host or DB (only `database update` needs Postgres).

### /home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Domain/Entities/ExecutionReport.cs
```csharp
public class ExecutionReport
{
    public Guid Id { get; set; }
    public string? ProgramId { get; set; }
    public string ProgramName { get; set; } = "";
    public Guid? UserId { get; set; }
    public string? UserName { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public int DurationSeconds { get; set; }
    public ExecutionStatus Status { get; set; }
    public int PeakTemp { get; set; }
    public decimal PeakCurrent { get; set; }
    public int? FaultAtT { get; set; }
    public int? FaultAtTemp { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public List<ExecProfilePoint> Points { get; set; } = new();
    public List<ProfileComparisonRow> Comparison { get; set; } = new();
    public FailureSnapshot Trace { get; set; } = new();
    public List<LogEvent> Events { get; set; } = new();
}
```
`FaultAtT` / `FaultAtTemp` are the existing precedent for nullable scalar columns on this entity.

### Migrations directory — /home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Infrastructure/Persistence/Migrations
Existing (chronological by timestamp prefix):
1. `20260530165250_AddFaultTypes`
2. `20260530223234_AddPasswordPolicy`
3. `20260531020103_AddNotificationFeed`
4. `20260531024822_AddUserPreferences`
5. `20260531025513_AddExecutionTrace` ← **latest** (timestamp `20260531025513`)
Plus `ReflowDbContextModelSnapshot.cs`. The next migration's timestamp prefix will be auto-generated and sort after this one.

The model snapshot confirms `Status` is stored as a bare text column with no constraint:
```csharp
// ReflowDbContextModelSnapshot.cs lines 255-257 (ExecutionReport) and 583-585 (ErrorLogEntry)
b.Property<string>("Status")
    .IsRequired()
    .HasColumnType("text");
```

Pattern for adding a scalar column to Executions (from `AddExecutionTrace` / `AddUserPreferences`):
```csharp
migrationBuilder.AddColumn<string>(
    name: "Trace", table: "Executions", type: "jsonb",
    nullable: false, defaultValue: "...");
// Down:
migrationBuilder.DropColumn(name: "Trace", table: "Executions");
```
For a NULLABLE scalar (the common case for item 4), the EF-generated body is simply `migrationBuilder.AddColumn<T>(name: "...", table: "Executions", type: "...", nullable: true);` with `DropColumn` in `Down` — no defaultValue/backfill needed.

## Change plan

1. **Item 6 (read-only enum change) — add new member(s) to an existing enum.** File `/home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Domain/Enums/Enums.cs`. For each new value add a line inside the target enum body, e.g. for `ExecutionStatus`:
   - old: `public enum ExecutionStatus { [JsonStringEnumMemberName("Concluído")] Concluido, Falha, }`
   - new: append `[JsonStringEnumMemberName("<exact-pt-BR-wire>")] <CSharpName>,` before the closing brace.
   No DbContext, no IAppDbContext, NO migration. The `text` column + reflection-based `EnumWire`/`PtBrEnumConverter` and the global `JsonStringEnumConverter` handle it automatically. Mirror the same literal in `../reflow-oven-front` (typically `src/lib/types.ts` / option lists) since it is contract.

3. **Item 3 (enum change, may be written/persisted).** Same edit in `Enums.cs` as step 1. If the enum is used as a non-jsonb column, the converter loop in `OnModelCreating` (lines 217-228) already wires `PtBrEnumConverter` for it — nothing else to do. If the new member should appear in seed/factory data, also update `/home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Application/Common/Defaults.cs`. Still no migration unless the enum's column type changes.

4. **Item 4 (migration — new scalar/FK).** 
   a. Add the property to the entity, e.g. on `ExecutionReport` (`/home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Domain/Entities/ExecutionReport.cs`): insert a property next to `FaultAtTemp`, e.g. `public int? NewField { get; set; }` (nullable scalar) or `public string? SomeFk { get; set; }` + the matching navigation for an FK.
   b. If it has length/precision/FK semantics, map it in `ReflowDbContext.OnModelCreating` inside the existing `b.Entity<ExecutionReport>(e => { ... })` block (line 88), following the sibling pattern: `e.Property(x => x.Field).HasMaxLength(...);` or `e.HasOne(...).WithMany(...).HasForeignKey(...).OnDelete(DeleteBehavior.Restrict);`. A plain nullable scalar needs no mapping.
   c. If it is a new ENTITY/table (not just a column): add a `DbSet<NewEntity>` to BOTH `/home/.../IAppDbContext.cs` and `ReflowDbContext.cs`, and a `b.Entity<NewEntity>(...)` block.
   d. Scaffold the migration (see Migration command below), then review the generated `Up`/`Down`. For a nullable scalar EF emits a single `AddColumn<T>(... nullable: true)` — accept as-is. For a NOT-NULL column on a table with existing rows, supply a `defaultValue` exactly like `AddExecutionTrace`/`AddUserPreferences` do, or the apply will fail.
   e. The migration applies automatically on API startup (`Database.MigrateAsync()` + `DbSeeder` in `Program.cs`); no manual `database update` needed except to test against a live DB.

## Migration?

- **Item 3 / Item 6 (enum changes): NO.** Enum columns are mapped as plain `text` (confirmed in the snapshot: `b.Property<string>("Status").IsRequired().HasColumnType("text")` for both `Executions` and `Errors`). There is NO `CHECK` constraint and NO PostgreSQL native enum type on any enum column — the only check constraints in the whole model are `CK_Users_Name` and the three `..._SingleRow` (`Id = 1`) singleton guards. Adding a new enum member changes neither the column type nor any constraint, and `EnumWire` discovers it by reflection, so no migration and no schema change are required. (The model snapshot won't even differ, so `migrations add` would produce an empty migration.)
- **Item 4 (new scalar/FK): YES.** Adding a property/column or a new table changes the relational model, so a migration is required. Columns/tables affected depend on the final design (e.g. a new nullable column on `Executions`, or a new table + FK). No CHECK/enum constraint blocks it; the only caveat is a NOT-NULL column on a populated table needs a `defaultValue` (existing precedent: `AddExecutionTrace`, `AddUserPreferences`).

## Contract/enum notes

- Every `[JsonStringEnumMemberName(...)]` literal is the wire AND the DB string — never change an existing one. Exact literals currently in use: `Parábola positiva`, `Parábola negativa`, `alvo/oven/board/current/voltage/ovenFan/boardFan`, `running/done/aborted`, `Concluído` (+ `Falha` with no attribute → stored as `"Falha"`), `info/alerta/falha`, `Crítico` (+ `Alerta`/`Aviso` bare), `config/program`, `INFO/Aviso/Erro`, `light/dark/system`, `Atenção/Crítica` (+ `Normal`/`Grave` bare), `Parar Processo/Continuar Processo`, `Contínuo` (+ `Pulsante`), `fan-oven/fan-board/buzzer/rs422/heater/thermocouple`, `idle/running/ok/fail`, `execucoes/alteracoes/falhas/logs/inativos`, `programmed/measured`, `added/removed/changed-before/changed-after/unchanged`, `power/control`.
- Members WITHOUT an attribute (e.g. `ExecutionStatus.Falha`, `ErrorSeverity.Alerta/Aviso`, `NotificationKind.Normal/Grave`, `ChangeAction.Criado/Editado/Removido`, `UserType.Admin/Regular`, `UserStatus.Ativo/Inativo`, `RunPhase.*`, `RampShape.Linear/Fixo`, `BuzzerSound.Pulsante`) serialize/persist as the exact C# member name (case-sensitive) — that name is itself the contract.
- When adding a new enum member, the wire literal must be agreed with and added to the frontend simultaneously. If a new value omits an attribute, the C# identifier becomes the wire literal (cannot contain accents/spaces) — usually you want an explicit `[JsonStringEnumMemberName("…")]` so the frontend gets the pt-BR text.
- `ExecutionStatus` (item 4/6 likely target) is the wire shape for Relatórios → Execuções; the frontend reads `Concluído`/`Falha`. Adding a third status means the front must handle the new string.
- DTOs are the wire contract (Application/Dtos); for item 4 a new column must also surface on the matching DTO + service mapping for the frontend to see it. Timestamps go out as ISO 8601 or null.

## Migration command (design-time)

```bash
export DOTNET_ROOT="$HOME/.dotnet"; export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"
dotnet ef migrations add <Name> -p src/ReflowOven.Infrastructure -s src/ReflowOven.Api -o Persistence/Migrations
```
`-p` = Infrastructure (migrations project), `-s` = Api (startup), `-o Persistence/Migrations` = output dir. The design-time factory supplies the connection string, so no running host/DB is needed to scaffold. Apply happens automatically on API startup, or manually:
```bash
dotnet ef database update -p src/ReflowOven.Infrastructure -s src/ReflowOven.Api   # needs Postgres up (docker compose up -d)
```
Suggested next migration name follows the latest (`20260531025513_AddExecutionTrace`); pick a descriptive `<Name>` for item 4.

## Risks / open questions

- Item 4 underspecified: is it a new nullable scalar on `ExecutionReport`, an FK to a new/existing table, or a brand-new entity? The plan branches (3a/3c) — confirm which. If it is a new entity, remember the DbSet must be added to BOTH `IAppDbContext` and `ReflowDbContext`, else the build/snapshot diverge.
- If item 4 adds a NOT-NULL column to the already-populated `Executions` table, a `defaultValue` is mandatory (mirror `AddExecutionTrace`); otherwise the auto-apply on startup will throw.
- Items 3/6: which enum(s)? If the change is to a JSON-stored enum (inside `Preferences`, `Profile`, `Segments`, `Points`, `Comparison`, `Trace`, `Snapshot`), the `PtBrEnumConverter` loop is skipped (`IsMappedToJson()` continue), but `System.Text.Json`'s global `JsonStringEnumConverter` still reads `[JsonStringEnumMemberName]`, so jsonb persistence stays correct — still no migration, but verify the exact jsonb literal matches the frontend.
- Adding an enum member is forward-compatible for reads, but old rows never contain the new value; any UI filter/grouping defaulting on the new value must tolerate absence.
- `EnumWire.FromWire` falls back to `Enum.Parse<TEnum>(wire)` for unknown strings and will THROW on a wire value not present as either an attribute or a member name — ensure any new member's literal is added before data using it is written.
- Frontend lock-step is a hard requirement (CLAUDE.md): any new enum literal or new DTO field must land in `../reflow-oven-front` (and `limits.ts` if a new cap is involved) in the same change set.