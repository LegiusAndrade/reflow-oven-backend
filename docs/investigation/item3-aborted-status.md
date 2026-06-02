# item3-aborted-status

I have all the code I need. Here is the precise report.

## Files

### /home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Domain/Enums/Enums.cs
The two relevant enums (lines 52-63):
```csharp
public enum RunStatus
{
    [JsonStringEnumMemberName("running")] Running,
    [JsonStringEnumMemberName("done")] Done,
    [JsonStringEnumMemberName("aborted")] Aborted,
}

public enum ExecutionStatus
{
    [JsonStringEnumMemberName("Concluído")] Concluido,
    Falha,
}
```
Note: `Falha` has no `[JsonStringEnumMemberName]`, so its wire value == its member name == `"Falha"`.

### /home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Infrastructure/Run/RunManager.cs
`StopAsync` (manual stop) calls `FinalizeAsync(run, RunStatus.Aborted, ct)` (line 95). The tick-driven completion calls `FinalizeAsync(run, RunStatus.Done, ct)` (line 131). `FinalizeAsync` in full (lines 139-196):
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
        logger.LogInformation("Execução finalizada ({Status}): '{Program}' (run {RunId}, {Duration}s, pico {Peak} °C).",
            status, run.ProgramName, run.RunId, duration, report.PeakTemp);
        await sink.PublishStatusAsync(run.RunId, status);
        await sink.PublishCompletedAsync(run.RunId, report.Id);
    }
```
Key lines: 155 (Status ternary — collapses everything non-`Done` to `Falha`), 167-168 (LogEvent kind + "Execução abortada"), 182-186 (Notification kind/title/message). Note `FinalizeAsync` is only ever reached with `RunStatus.Done` (line 131) or `RunStatus.Aborted` (line 95) — `Running` never flows in, so the binary `Done`-vs-rest ternary is currently always "rest == Aborted".

### /home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Application/Services/ReportService.cs
`ExecutionsAsync` (lines 7-27) — the status filter on line 17:
```csharp
    public async Task<PagedResult<ExecutionSummaryDto>> ExecutionsAsync(ReportQuery q, CancellationToken ct = default)
    {
        IQueryable<ExecutionReport> query = db.Executions;
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var s = q.Search.Trim().ToLower();
            query = query.Where(e => e.ProgramName.ToLower().Contains(s) || (e.UserName != null && e.UserName.ToLower().Contains(s)));
        }
        if (q.From is not null) query = query.Where(e => e.StartedAt >= q.From);
        if (ToExclusive(q) is { } toExc) query = query.Where(e => e.StartedAt < toExc);
        if (EnumWire.TryFromWire<ExecutionStatus>(q.Status, out var status)) query = query.Where(e => e.Status == status);

        var total = await query.CountAsync(ct);
        var (page, size) = Paging(q);
        var items = await query
            .OrderByDescending(e => e.StartedAt)
            .Skip((page - 1) * size).Take(size)
            .Select(e => new ExecutionSummaryDto(e.Id, e.ProgramId, e.ProgramName, e.UserName, e.StartedAt, e.DurationSeconds, e.Status, e.PeakTemp, e.PeakCurrent))
            .ToListAsync(ct);
        return new PagedResult<ExecutionSummaryDto>(items, total, page, size);
    }
```
`ExecutionAsync` (lines 29-40) — passes `e.Status` straight through, no transformation:
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
The filter helper it relies on — `/home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Domain/Common/EnumWire.cs`, `TryFromWire` (lines 40-49):
```csharp
    public static bool TryFromWire<TEnum>(string? wire, out TEnum value) where TEnum : struct, Enum
    {
        if (wire is not null && MapsFor(typeof(TEnum)).fromWire.TryGetValue(wire, out var v))
        {
            value = (TEnum)v;
            return true;
        }
        value = default;
        return false;
    }
```
`MapsFor` (lines 18-31) builds `fromWire` keyed on `JsonStringEnumMemberNameAttribute.Name ?? f.Name` — so a member named `Abortado` with no attribute is filterable as `?status=Abortado` with zero service changes.

### /home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Infrastructure/Persistence/ReflowDbContext.cs
The `ExecutionReport` mapping block (lines 88-104). The `Status` column has **no explicit per-property mapping and NO CHECK constraint**:
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
            e.OwnsOne(x => x.Trace, s =>
            {
                s.ToJson();
                s.OwnsMany(z => z.Series);
            });
            e.HasMany(x => x.Events).WithOne(v => v.ExecutionReport!).HasForeignKey(v => v.ExecutionReportId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.StartedAt);
        });
```
The `Status` enum column is instead handled by the global loop at the bottom of `OnModelCreating` (lines 217-228), which attaches `PtBrEnumConverter<ExecutionStatus>` (stores enum as its wire text in a plain `text` column):
```csharp
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
```
Confirmed in the migration / snapshot that the DB column is plain text with no CHECK:
- `/home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Infrastructure/Persistence/Migrations/20260530165250_AddFaultTypes.cs:98` → `Status = table.Column<string>(type: "text", nullable: false),` and the `Executions` `constraints` block (lines 107-110) contains **only** `table.PrimaryKey("PK_Executions", x => x.Id);` — no CheckConstraint.
- `ReflowDbContextModelSnapshot.cs:255-257` → `b.Property<string>("Status").IsRequired().HasColumnType("text");` — no constraint.

## Change plan

1. **File `src/ReflowOven.Domain/Enums/Enums.cs`, enum `ExecutionStatus` (lines 59-63).** Add a third member named `Abortado` with **no** `[JsonStringEnumMemberName]` attribute (so both the JSON response literal and the filter member-name resolve to the bare `"Abortado"`, exactly like `Falha`). Insert it after `Falha`.
   - old:
     ```csharp
     public enum ExecutionStatus
     {
         [JsonStringEnumMemberName("Concluído")] Concluido,
         Falha,
     }
     ```
   - new:
     ```csharp
     public enum ExecutionStatus
     {
         [JsonStringEnumMemberName("Concluído")] Concluido,
         Falha,
         Abortado,
     }
     ```

2. **File `src/ReflowOven.Infrastructure/Run/RunManager.cs`, method `FinalizeAsync`, line 155.** Replace the binary ternary with a `switch` expression so a manual/abort run maps to the new `Abortado`, a completed run to `Concluido`, and any future non-terminal value falls back to `Falha`.
   - old:
     ```csharp
     Status = status == RunStatus.Done ? ExecutionStatus.Concluido : ExecutionStatus.Falha,
     ```
   - new:
     ```csharp
     Status = status switch
     {
         RunStatus.Done => ExecutionStatus.Concluido,
         RunStatus.Aborted => ExecutionStatus.Abortado,
         _ => ExecutionStatus.Falha,
     },
     ```

3. **No other code change is required to satisfy the TODO's three surfaces.** Confirm (do not edit):
   - **List** (`ExecutionsAsync`, ReportService.cs:24) and **Detail** (`ExecutionAsync`, ReportService.cs:34) already pass `e.Status` through unchanged into `ExecutionSummaryDto.Status` / `ExecutionDetailDto.Status` (both typed `ExecutionStatus`), so the new value serializes as `"Abortado"` automatically.
   - **Filter** (`ReportService.cs:17` `EnumWire.TryFromWire<ExecutionStatus>(q.Status, ...)`): because `Abortado` has no attribute, `MapsFor` registers `fromWire["Abortado"] = Abortado`, so `?status=Abortado` resolves with no service edit.
   - **Persistence**: the global converter loop (ReflowDbContext.cs:217-228) auto-attaches `PtBrEnumConverter<ExecutionStatus>`, writing the text `"Abortado"` into the existing `text` column.

4. **Optional decisions left to the implementer (TODO line 353) — not strictly required, current values stay coherent:**
   - The abort `LogEvent` (RunManager.cs:167-168) currently emits `LogEventKind.Alerta` + "Execução abortada". You may keep it, or switch the kind to a neutral one if a less alarming log icon is desired. The wording is already correct.
   - The notification (RunManager.cs:182-186) emits `NotificationFeedKind.Error` + "Execução abortada" / "foi abortada após Ns". Optionally downgrade `Error` → `Info` since a manual stop is not a defect; the title/message already read as an abort. Leaving these untouched is contradiction-free.
   - `SystemService.cs:105-106` counts only `Concluido` and `Falha`. Aborted runs will now be excluded from both buckets. If the maintenance overview's exec totals should account for aborts, add a third `CountAsync(e => e.Status == ExecutionStatus.Abortado, ct)` — out of scope for this TODO but worth flagging.

## Migration?

**No.** The `ExecutionStatus` column (`Executions.Status`) is a plain PostgreSQL `text` column (`type: "text"` in `20260530165250_AddFaultTypes.cs:98`; `HasColumnType("text")` in the snapshot at line 257). It is stored via `PtBrEnumConverter<ExecutionStatus>` (a CLR-side `ValueConverter<TEnum, string>`), not a native PG enum and not constrained — the `Executions` table's only constraint is `PK_Executions`. Adding a CLR enum member changes only the set of strings the app may write; it does not alter the column type, and EF's value-converter properties are not tracked in the model snapshot as schema, so `migrations add` would produce an empty migration. The only DB-level CHECK constraints in the whole model are `CK_Users_Name`, `CK_Settings_SingleRow`, `CK_Calibration_SingleRow`, `CK_DeviceInfo_SingleRow` — none on any enum/status column. Existing `"Falha"` rows from prior manual stops keep their value (not retroactively reclassified).

## Contract/enum notes

- New member **`Abortado`** must carry **NO** `[JsonStringEnumMemberName]` — by design its wire/response literal and its filter member-name both equal the bare ASCII `"Abortado"` (the unaccented Portuguese word), mirroring how `Falha` works. Adding an accented attribute would split the response value from the filter key and break `?status=`.
- Do not touch the existing literals: `Concluido` → `"Concluído"` (accented, via attribute) and `Falha` → `"Falha"`. These are the frozen API contract.
- `RunStatus` (`running`/`done`/`aborted`) is a **separate** enum (live-run/SignalR `RunStatusChanged`), unchanged; only `ExecutionStatus` (persisted report) gains a member.
- DTO shapes the frontend depends on are unchanged — `ExecutionSummaryDto.Status` (ReportDtos.cs:23) and `ExecutionDetailDto.Status` (ReportDtos.cs:35) are already typed `ExecutionStatus` and pass through verbatim; the front just gains a third possible string. Per the TODO (lines 367-371) the frontend must then widen `ExecutionStatusWire`/`ExecutionStatus` to include `"Abortado"`, make `StatusBadge` a per-status map (e.g. `Abortado` → amber `stop_circle`), and add the `Abortado` filter option — frontend work, outside this backend change.

## Risks / open questions

- **Retroactive data:** runs manually stopped before this change are already persisted as `"Falha"` and will not become `"Abortado"`. If demo/seed data or QA expects historical aborts to show the new badge, a one-off backfill/reseed would be needed. `DbSeeder.Demo.cs:155` only seeds `Falha`/`Concluido`; consider adding `Abortado` demo rows if the frontend reviewer wants to see the badge without running a real abort.
- **Frontend lockstep (CLAUDE.md rule):** shipping the backend member without the matching frontend changes (TODO lines 367-371) means the UI will render an unmapped status — current `StatusBadge` logic (`ok = status === "Concluído"`, else red) would paint an abort as a red "Falha"-style badge, partially defeating the feature. Coordinate the two repos.
- **`SystemService` exec counts** (lines 105-106) silently drop aborted runs from both `execConcluido` and `execFalha`. Decide whether the maintenance overview should add an `Abortado` bucket — not part of TODO item 3 but a behavioral side effect.
- **Notification/log kind decision** (TODO line 353) is unresolved: keep `Alerta`/`Error` (treats abort like a failure) or neutralize them. Pure product choice; either compiles.
- **Default-value safety:** `EnumWire.TryFromWire` sets `value = default` (= `Concluido`, ordinal 0) when it returns false, but callers guard on the bool, so an unknown `?status=` is correctly ignored rather than silently filtering to `Concluido`. No regression from adding a member.