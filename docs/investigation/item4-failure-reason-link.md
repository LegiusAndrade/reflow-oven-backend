# item4-failure-reason-link

## Files

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

    /// <summary>Raw seconds (formatted as "5m 12s" on the client).</summary>
    public int DurationSeconds { get; set; }

    public ExecutionStatus Status { get; set; }

    public int PeakTemp { get; set; }
    public decimal PeakCurrent { get; set; }

    /// <summary>Set only on failed runs: when (s) and at what temperature (°C) the fault hit.</summary>
    public int? FaultAtT { get; set; }
    public int? FaultAtTemp { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Programmed + measured curve points (Kind discriminates). Stored as jsonb.</summary>
    public List<ExecProfilePoint> Points { get; set; } = new();

    /// <summary>Per-stage programmed-vs-real comparison rows. Stored as jsonb.</summary>
    public List<ProfileComparisonRow> Comparison { get; set; } = new();

    /// <summary>Downsampled multi-signal trace ... Owned/jsonb.</summary>
    public FailureSnapshot Trace { get; set; } = new();

    /// <summary>Event timeline (shared table with ErrorLogEntry).</summary>
    public List<LogEvent> Events { get; set; } = new();
}
```
(`LogEvent` has `Guid? ExecutionReportId`, `Guid? ErrorLogEntryId` — both nullable FKs, exactly one set.)

### /home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Domain/Entities/ErrorLogEntry.cs
```csharp
/// <summary>Seeded fault catalog (E-101..E-160)...</summary>
public class FaultType
{
    /// <summary>Stable code, e.g. "E-101". Primary key.</summary>
    public string Code { get; set; } = "";
    public ErrorSeverity Severity { get; set; }
    public string Message { get; set; } = "";

    public ICollection<ErrorLogEntry> Errors { get; set; } = new List<ErrorLogEntry>();
}

public class ErrorLogEntry
{
    public Guid Id { get; set; }
    public DateTimeOffset At { get; set; }

    public string FaultTypeCode { get; set; } = "";
    public FaultType? FaultType { get; set; }

    public ErrorSeverity Severity { get; set; }
    public string Message { get; set; } = "";

    public Guid? UserId { get; set; }
    public string? UserName { get; set; }
    public string? ProgramId { get; set; }
    public string? ProgramName { get; set; }

    public int OvenTemp { get; set; }
    public int PcbTemp { get; set; }
    public DateTimeOffset StartAt { get; set; }
    public DateTimeOffset EndAt { get; set; }

    public int InputVoltage { get; set; }   // VAC ~127
    public int OutputVoltage { get; set; }  // VDC 0..180

    public FailureSnapshot Snapshot { get; set; } = new();   // owned/jsonb
    public List<LogEvent> Events { get; set; } = new();
}
```
FaultType PK = `Code` (string, maxlen 10). The `Errors` FK is `OnDelete(DeleteBehavior.Restrict)`.

### /home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Infrastructure/Run/RunManager.cs — `FinalizeAsync` (lines 139-196)
```csharp
private async Task FinalizeAsync(ActiveRun run, RunStatus status, CancellationToken ct)
{
    await board.StopAsync(ct);
    run.Status = status;
    var endedAt = clock.UtcNow;
    var duration = (int)Math.Round((endedAt - run.StartedAt).TotalSeconds);

    var report = new ExecutionReport
    {
        Id = run.RunId,
        ProgramId = run.ProgramId,
        ProgramName = run.ProgramName,
        UserId = run.UserId,
        UserName = run.UserName,
        StartedAt = run.StartedAt,
        DurationSeconds = duration,
        Status = status == RunStatus.Done ? ExecutionStatus.Concluido : ExecutionStatus.Falha,
        PeakTemp = (int)Math.Round(run.PeakTemp),
        PeakCurrent = (decimal)Math.Round(run.PeakCurrent, 1),
        CreatedAt = endedAt,
        Points = BuildPoints(run),
        Trace = BuildTrace(run, duration),
        Events =
        [
            new LogEvent { At = run.StartedAt, Kind = LogEventKind.Info, Message = "Execução iniciada", OrderIndex = 0 },
            new LogEvent
            {
                At = endedAt,
                Kind = status == RunStatus.Done ? LogEventKind.Info : LogEventKind.Alerta,
                Message = status == RunStatus.Done ? "Execução concluída" : "Execução abortada",
                OrderIndex = 1,
            },
        ],
    };

    using (var scope = scopeFactory.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        db.Executions.Add(report);
        db.Notifications.Add(new Notification
        {
            Id = Guid.NewGuid(),
            At = endedAt,
            Kind = status == RunStatus.Done ? NotificationFeedKind.Info : NotificationFeedKind.Error,
            Title = status == RunStatus.Done ? "Execução concluída" : "Execução abortada",
            Message = status == RunStatus.Done
                ? $"'{run.ProgramName}' concluída em {duration}s (pico {report.PeakTemp} °C)."
                : $"'{run.ProgramName}' foi abortada após {duration}s.",
        });
        await db.SaveChangesAsync(ct);
    }

    _active = null;
    logger.LogInformation("Execução finalizada ...");
    await sink.PublishStatusAsync(run.RunId, status);
    await sink.PublishCompletedAsync(run.RunId, report.Id);
}
```
KEY FINDINGS:
- `FaultAtT`/`FaultAtTemp` are **never set** here — the live run path leaves them null even on `RunStatus.Aborted`/`Falha`. Only `DbSeeder.Demo` sets them.
- `RunStatus.Aborted` and `RunStatus.Done` are the only two statuses FinalizeAsync is ever called with (from `StopAsync` and `TickAsync`). There is **no real-fault status** flowing in. `Aborted` maps to `ExecutionStatus.Falha`.
- The board (`IPowerBoard.ReadAsync`) returns a `BoardReading` with no fault-code field (confirm during impl — see open questions). The simulator never raises a fault.

### /home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Application/Dtos/ReportDtos.cs — `ExecutionSummaryDto` + `ExecutionDetailDto`
```csharp
public sealed record ExecutionSummaryDto(
    Guid Id, string? ProgramId, string ProgramName, string? UserName,
    DateTimeOffset StartedAt, int DurationSeconds, ExecutionStatus Status,
    int PeakTemp, decimal PeakCurrent);

public sealed record ExecutionDetailDto(
    Guid Id, string? ProgramId, string ProgramName, Guid? UserId, string? UserName,
    DateTimeOffset StartedAt, int DurationSeconds, ExecutionStatus Status,
    int PeakTemp, decimal PeakCurrent,
    int? FaultAtT, int? FaultAtTemp,
    IReadOnlyList<ExecProfilePointDto> Points,
    IReadOnlyList<ProfileComparisonRowDto> Comparison,
    IReadOnlyList<LogEventDto> Events,
    FailureSnapshotDto Trace);
```

### /home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Application/Services/ReportService.cs — `ExecutionAsync` (lines 29-40)
```csharp
public async Task<ExecutionDetailDto> ExecutionAsync(Guid id, CancellationToken ct = default)
{
    var e = await db.Executions.Include(x => x.Events).FirstOrDefaultAsync(x => x.Id == id, ct)
        ?? throw new NotFoundException("Execução não encontrada.");
    return new ExecutionDetailDto(
        e.Id, e.ProgramId, e.ProgramName, e.UserId, e.UserName, e.StartedAt, e.DurationSeconds, e.Status,
        e.PeakTemp, e.PeakCurrent, e.FaultAtT, e.FaultAtTemp,
        e.Points.Select(p => new ExecProfilePointDto(p.T, p.Temp, p.Kind)).ToList(),
        e.Comparison.Select(c => new ProfileComparisonRowDto(c.TempProg, c.TempReal, c.TimeProgSeconds, c.TimeRealSeconds, c.StageIndex)).ToList(),
        e.Events.OrderBy(v => v.OrderIndex).ThenBy(v => v.At).Select(MapEvent).ToList(),
        MapSnapshot(e.Trace));
}
```

### /home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Infrastructure/Persistence/ReflowDbContext.cs — relevant mappings
ExecutionReport (lines 88-104):
```csharp
b.Entity<ExecutionReport>(e =>
{
    e.HasKey(x => x.Id);
    e.Property(x => x.ProgramName).HasMaxLength(120);
    e.Property(x => x.UserName).HasMaxLength(40);
    e.Property(x => x.ProgramId).HasMaxLength(64);
    e.Property(x => x.PeakCurrent).HasPrecision(5, 1);
    e.OwnsMany(x => x.Points, o => o.ToJson());
    e.OwnsMany(x => x.Comparison, o => o.ToJson());
    e.OwnsOne(x => x.Trace, s => { s.ToJson(); s.OwnsMany(z => z.Series); });
    e.HasMany(x => x.Events).WithOne(v => v.ExecutionReport!).HasForeignKey(v => v.ExecutionReportId).OnDelete(DeleteBehavior.Cascade);
    e.HasIndex(x => x.StartedAt);
});
```
FaultType (112-117) + ErrorLogEntry (119-133):
```csharp
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
    e.OwnsOne(x => x.Snapshot, s => { s.ToJson(); s.OwnsMany(z => z.Series); });
    e.HasOne(x => x.FaultType).WithMany(f => f.Errors).HasForeignKey(x => x.FaultTypeCode).OnDelete(DeleteBehavior.Restrict);
    e.HasMany(x => x.Events).WithOne(v => v.ErrorLogEntry!).HasForeignKey(v => v.ErrorLogEntryId).OnDelete(DeleteBehavior.Cascade);
    e.HasIndex(x => x.At);
});
```
The generic enum loop (217-228) applies `PtBrEnumConverter<>` to every non-JSON enum column automatically — no manual converter needed for new enum columns (but our new fields are `string?`/`Guid?`, not enums).

### /home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Application/Common/Defaults.cs — fault catalog (lines 30-40)
```csharp
public static List<FaultType> FaultTypes() =>
[
    new() { Code = "E-101", Severity = ErrorSeverity.Critico, Message = "Sobretemperatura na grelha (termopar tipo-K)" },
    new() { Code = "E-102", Severity = ErrorSeverity.Critico, Message = "Falha de leitura do termopar tipo-K" },
    new() { Code = "E-110", Severity = ErrorSeverity.Critico, Message = "Sobrecorrente detectada (sensor Hall)" },
    new() { Code = "E-120", Severity = ErrorSeverity.Alerta,  Message = "Tensão de saída fora da faixa (0–180 VDC)" },
    new() { Code = "E-130", Severity = ErrorSeverity.Alerta,  Message = "Perda de comunicação RS422 com a placa de potência" },
    new() { Code = "E-140", Severity = ErrorSeverity.Alerta,  Message = "Dissipador acima do limite (NTC)" },
    new() { Code = "E-150", Severity = ErrorSeverity.Aviso,   Message = "Ventoinha 1 com rotação abaixo do esperado" },
    new() { Code = "E-160", Severity = ErrorSeverity.Aviso,   Message = "Subtensão na entrada 127 VAC" },
]; 
```
`BuildError` lives in **DbSeeder.Demo.cs** (lines 167-198, quoted above) — it builds `ErrorLogEntry` from a `FaultType`, copying `FaultTypeCode`/`Severity`/`Message` and an owned `Snapshot`. Defaults.cs has no `BuildError`.

### Enum wire literals (Enums/Enums.cs)
```csharp
public enum ExecutionStatus { [JsonStringEnumMemberName("Concluído")] Concluido, Falha }
public enum ErrorSeverity   { [JsonStringEnumMemberName("Crítico")] Critico, Alerta, Aviso }
public enum LogEventKind    { [JsonStringEnumMemberName("info")] Info, [JsonStringEnumMemberName("alerta")] Alerta, [JsonStringEnumMemberName("falha")] Falha }
```

### IAppDbContext.cs (relevant DbSets)
```csharp
DbSet<ExecutionReport> Executions { get; }
DbSet<FaultType> FaultTypes { get; }
DbSet<ErrorLogEntry> Errors { get; }
```
Error-detail route: `GET /api/errors/{id:guid}` → `ReportService.ErrorAsync` (ReportControllers.cs lines 27-36). This is the link target the frontend would deep-link to with `LinkedErrorId`.

## Change plan

1. **ExecutionReport.cs** — add three nullable fields after the existing `FaultAtTemp` (line 30):
   ```csharp
   /// <summary>Set only on a real (non-abort) fault: the catalog code (E-1xx) and human reason.</summary>
   public string? FaultTypeCode { get; set; }
   public string? FailureReason { get; set; }
   /// <summary>FK to the ErrorLogEntry created for this fault, for the "ver Relatório de Erro" link. Null for clean/aborted runs.</summary>
   public Guid? LinkedErrorId { get; set; }
   ```
   Do NOT add a navigation property to `ErrorLogEntry` (keep it a loose `Guid?` to avoid a delete-behavior cycle with the existing `LogEvent`↔`ErrorLogEntry` cascade; the frontend only needs the id to build the link).

2. **ReflowDbContext.cs** — inside `b.Entity<ExecutionReport>(e => {...})` (after line 94, the `PeakCurrent` precision line) add column constraints:
   ```csharp
   e.Property(x => x.FaultTypeCode).HasMaxLength(10);
   e.Property(x => x.FailureReason).HasMaxLength(200);
   e.HasIndex(x => x.LinkedErrorId);
   ```
   Do NOT call `.HasOne(...).WithMany()` for `LinkedErrorId` — leave it as a plain scalar column so EF does not create a real FK that would conflict with the `Restrict`/`Cascade` graph. (If a true FK is wanted, use `.HasOne<ErrorLogEntry>().WithMany().HasForeignKey(x => x.LinkedErrorId).OnDelete(DeleteBehavior.SetNull)` — but the simplest, lowest-risk choice is no FK.)

3. **ReportDtos.cs** — extend `ExecutionDetailDto` to carry the new fields. Append after `int? FaultAtTemp,` (line 39):
   ```csharp
   int? FaultAtT,
   int? FaultAtTemp,
   string? FaultTypeCode,
   string? FailureReason,
   Guid? LinkedErrorId,
   IReadOnlyList<ExecProfilePointDto> Points,
   ...
   ```
   (Insert the three new params between `FaultAtTemp` and `Points`.) `ExecutionSummaryDto` does NOT need them — the link/reason is a detail-only concern. Optionally add `Guid? LinkedErrorId` to the summary if the list should show a fault badge; not required by the task.

4. **ReportService.cs** `ExecutionAsync` — update the `new ExecutionDetailDto(...)` projection to pass the new fields. Change:
   ```csharp
   e.PeakTemp, e.PeakCurrent, e.FaultAtT, e.FaultAtTemp,
   e.Points.Select(...)
   ```
   to:
   ```csharp
   e.PeakTemp, e.PeakCurrent, e.FaultAtT, e.FaultAtTemp,
   e.FaultTypeCode, e.FailureReason, e.LinkedErrorId,
   e.Points.Select(...)
   ```

5. **RunManager.cs** `FinalizeAsync` — add a fault parameter and the error-creation branch. Concrete steps:
   - Change the signature to accept an optional fault descriptor:
     ```csharp
     private async Task FinalizeAsync(ActiveRun run, RunStatus status, FaultInfo? fault, CancellationToken ct)
     ```
     where `FaultInfo` is a new small record (declare it near `ActiveRun`): `private sealed record FaultInfo(string Code, ErrorSeverity Severity, string Message, int AtT, int AtTemp);`
   - Update the two call sites: `StopAsync` (line 95) → `await FinalizeAsync(run, RunStatus.Aborted, null, ct);` and `TickAsync` (line 131) → `await FinalizeAsync(run, RunStatus.Done, null, ct);`. (No real fault source exists yet in the simulator path, so both pass `null` — see open questions.)
   - In the report initializer, when `fault is { } f`, set `FaultAtT = f.AtT`, `FaultAtTemp = f.AtTemp`, `FaultTypeCode = f.Code`, `FailureReason = f.Message`. Leave them null otherwise. The status mapping must become: `Concluido` if `Done`, else `Falha` (unchanged), but the **fault** branch is what distinguishes a real fault from a user abort.
   - Inside the `using (scope)` block, before `db.Executions.Add(report)`, when `fault is { } f` create + add an `ErrorLogEntry` and set `report.LinkedErrorId`:
     ```csharp
     if (fault is { } f)
     {
         var error = new ErrorLogEntry
         {
             Id = Guid.NewGuid(),
             At = endedAt,
             FaultTypeCode = f.Code,
             Severity = f.Severity,
             Message = f.Message,
             UserId = run.UserId,
             UserName = run.UserName,
             ProgramId = run.ProgramId,
             ProgramName = run.ProgramName,
             OvenTemp = (int)Math.Round(run.Last?.Oven ?? 0),
             PcbTemp = (int)Math.Round(run.Last?.Board ?? 0),
             StartAt = run.StartedAt,
             EndAt = endedAt,
             InputVoltage = 127,
             OutputVoltage = (int)Math.Round(run.Last?.Voltage ?? 0),
             Snapshot = BuildTrace(run, duration),   // reuse the same downsampled snapshot
             Events =
             [
                 new LogEvent { At = endedAt, Kind = LogEventKind.Falha, Message = f.Message, OrderIndex = 0 },
             ],
         };
         db.Errors.Add(error);
         report.LinkedErrorId = error.Id;
     }
     db.Executions.Add(report);
     ```
     Note `db.Errors` is already on `IAppDbContext`. The `FaultTypeCode` must reference an existing seeded `FaultType.Code` (FK is `Restrict`) — pass only catalog codes (E-101..E-160).
   - The notification `Kind`/`Title`/`Message` for a real fault should use `NotificationFeedKind.Error` and a fault-specific message; the existing abort branch already uses `Error`. Optionally branch on `fault is not null` for a clearer "Falha: {code}" title.

   **Heuristic note:** today nothing produces a `FaultInfo`. For the abort path (`StopAsync`) and clean done path, `fault` stays `null` → `FailureReason`/`FaultTypeCode`/`LinkedErrorId` remain null and no `ErrorLogEntry` is created (matches CLAUDE.md: simulator has no real fault code). A future real-fault source (RS422 board raising an `E-1xx`, or a limit breach detected in `TickAsync` comparing `reading` to `ProcessLimits`) would call `FinalizeAsync(run, RunStatus.Aborted, new FaultInfo(...), ct)`. Keep the plumbing in place even though the simulator never triggers it.

6. (Optional, recommended) **DbSeeder.Demo.cs** `BuildExecution` — to exercise the new link in dev, when `failed`, set `report.FaultTypeCode`/`FailureReason`/`LinkedErrorId` to one of the demo `ErrorLogEntry` rows. This requires coordinating the executions and errors loops (currently independent). Lower priority; can be skipped to keep the migration-focused change minimal.

## Migration?
**yes.** Three new columns on the existing `Executions` table: `FaultTypeCode text NULL` (varchar(10)), `FailureReason text NULL` (varchar(200)), `LinkedErrorId uuid NULL`, plus an index `IX_Executions_LinkedErrorId`. No CHECK or enum constraint blocks this — all three are nullable scalars (no enum, so no `PtBrEnumConverter` and no enum DB literal). No new table. If you choose to add a real FK in step 2 it would also emit an `AddForeignKey` to `Errors(Id)` with `ON DELETE SET NULL`; the recommended no-FK approach emits only `AddColumn` + `CreateIndex`.

Exact command (matching the project's migration directory and naming):
```bash
export DOTNET_ROOT="$HOME/.dotnet"; export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"
dotnet ef migrations add AddExecutionFailureLink \
  -p src/ReflowOven.Infrastructure -s src/ReflowOven.Api -o Persistence/Migrations
```
Migration applies automatically on API startup (`Database.MigrateAsync()` in `Program.cs`), so no separate `database update` is needed for normal runs (it needs Postgres up via `docker compose up -d`).

## Contract/enum notes
- `ExecutionStatus`: `Concluido` → wire `"Concluído"`; `Falha` → `"Falha"` (no attribute, member name). Do not change.
- `ErrorSeverity`: `Critico` → `"Crítico"`; `Alerta`/`Aviso` plain. The `ErrorLogEntry.Severity` you write must come from the seeded `FaultType.Severity` so DB text matches.
- `FaultTypeCode` values must be exact catalog codes (`E-101`..`E-160`) — they are the FK to `FaultType.Code` (`Restrict` delete) and must already be seeded; an unknown code will fail the insert.
- `LogEventKind`: `Info`/`Alerta`/`Falha` → `"info"`/`"alerta"`/`"falha"`. The error's timeline event should use `LogEventKind.Falha`.
- DTO shape: `ExecutionDetailDto` is positional (record) and consumed by JSON by property name, so adding fields is backward-compatible for the frontend as long as names are `faultTypeCode`/`failureReason`/`linkedErrorId` (camelCased by the serializer). The frontend uses `linkedErrorId` to deep-link to `GET /api/errors/{id}` (the Erros detail overlay). `ExecutionSummaryDto` left unchanged (no contract change to the list).
- `FailureReason`/`FaultTypeCode`/`LinkedErrorId` are nullable and will be `null` for all existing rows and for every simulator/abort run — the client must treat them as optional ("sem falha registrada").

## Risks / open questions
- **No real fault source exists.** `IPowerBoard.ReadAsync`/`BoardReading` (Domain/Hardware/HardwareTypes.cs) was not confirmed to carry a fault code; the simulator never raises one and `StopAsync`/`TickAsync` only pass `Done`/`Aborted`. So in practice the new fields stay null until a real-fault detection path is added. Confirm whether `BoardReading` has any fault/limit field before deciding the `FaultInfo` source; otherwise the only honest trigger today is a server-side limit-breach check in `TickAsync` (compare `reading` to the `ProcessLimits` captured at start) — that is a larger change than the task scopes.
- **Abort vs fault semantics.** Currently `RunStatus.Aborted` → `ExecutionStatus.Falha`. The task says abort/no-fault keeps the new fields null, which means a user-aborted run still shows `Status = Falha` but with no reason/link. Confirm the frontend distinguishes "Falha (abortada pelo usuário)" from "Falha (com erro registrado, ver relatório)" purely by `linkedErrorId != null`.
- **FK choice for LinkedErrorId.** Plain `Guid?` (no FK) is simplest and avoids cascade-cycle issues with the existing `Errors`→`LogEvents` cascade and `Errors`→`FaultType` restrict. A real FK with `SetNull` is cleaner referential integrity but adds a constraint and a second migration concern. Recommend no-FK unless the user wants enforced integrity.
- **OvenTemp/PcbTemp/voltage for the ErrorLogEntry** are taken from `run.Last` (last tick sample). If a fault fires before any tick, `run.Last` is null → defaults (0 / 127). Acceptable but worth a comment.
- **`InputVoltage = 127` is hardcoded** in the error row (the sim has no mains-input channel). Flag if a real value is available.
- **DbSeeder.Demo coupling (step 6).** Wiring demo executions to demo errors requires reordering two independent loops; skipping it leaves dev data with null links on failed executions. Decide whether dev parity matters for this item.