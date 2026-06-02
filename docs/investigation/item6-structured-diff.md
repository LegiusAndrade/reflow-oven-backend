# item6-structured-diff

I now have everything needed. Note the seed `EditDiff` emits a "changed" point with the SAME index appearing twice (ChangedBefore + ChangedAfter), but the seeded `Removed` at index `curve.Count` and `Added` at index `curve.Count + 1` — confirming consolidation must collapse by (index + role-pair), not solely by index, since added/removed can share index space. I have enough to write the report.

## Files

### /home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Application/Services/ReportService.cs

`ChangesAsync` (lines 77-98) and `ChangeAsync` (lines 100-108):

```csharp
    // --- changes --------------------------------------------------------------------------
    public async Task<PagedResult<ChangeSummaryDto>> ChangesAsync(ReportQuery q, CancellationToken ct = default)
    {
        IQueryable<ChangeLogEntry> query = db.Changes;
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var s = q.Search.Trim().ToLower();
            query = query.Where(c => c.Target.ToLower().Contains(s) || (c.UserName != null && c.UserName.ToLower().Contains(s)));
        }
        if (q.From is not null) query = query.Where(c => c.At >= q.From);
        if (ToExclusive(q) is { } toExc) query = query.Where(c => c.At < toExc);
        if (EnumWire.TryFromWire<ChangeAction>(q.Action, out var action)) query = query.Where(c => c.Action == action);
        if (!string.IsNullOrWhiteSpace(q.ProgramId)) query = query.Where(c => c.ProgramId == q.ProgramId);

        var total = await query.CountAsync(ct);
        var (page, size) = Paging(q);
        var items = await query
            .OrderByDescending(c => c.At)
            .Skip((page - 1) * size).Take(size)
            .Select(c => new ChangeSummaryDto(c.Id, c.At, c.Action, c.Target, c.UserName, c.DetailKind))
            .ToListAsync(ct);
        return new PagedResult<ChangeSummaryDto>(items, total, page, size);
    }

    public async Task<ChangeDetailDto> ChangeAsync(Guid id, CancellationToken ct = default)
    {
        var c = await db.Changes.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException("Alteração não encontrada.");
        return new ChangeDetailDto(
            c.Id, c.At, c.Action, c.Target, c.UserName, c.ProgramId, c.DetailKind,
            c.ConfigBullets,
            c.Points.OrderBy(p => p.Index).Select(p => new ChangePointRowDto(p.Index, p.Temp, p.TimeSec, p.Ramp, p.Role)).ToList());
    }
```

Supporting helpers (lines 136-149), used by the plan:

```csharp
    // The page size is clamped server-side so a client can never pull an unbounded result set
    // (e.g. "give me 1000 rows") and overload the service — at most ReportPageSizeMax rows.
    private static (int page, int size) Paging(ReportQuery q) =>
        (Math.Max(1, q.Page), Math.Clamp(q.PageSize, 1, DomainConstants.ReportPageSizeMax));

    // Treat the inclusive 'To' date as the end of that day: filter strictly below the next midnight,
    // so a yyyy-mm-dd value (which parses to 00:00) still includes rows from that whole day.
    private static DateTimeOffset? ToExclusive(ReportQuery q) =>
        q.To is { } to ? new DateTimeOffset(to.Date.AddDays(1), to.Offset) : null;
```

### /home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Application/Dtos/ReportDtos.cs

Changes DTOs (lines 78-98) and `ReportQuery` (lines 105-121):

```csharp
// --- changes ----------------------------------------------------------------------------
public sealed record ChangePointRowDto(int Index, int Temp, int TimeSec, RampShape Ramp, ChangePointRole Role);

public sealed record ChangeSummaryDto(
    Guid Id,
    DateTimeOffset At,
    ChangeAction Action,
    string Target,
    string? UserName,
    ChangeDetailKind DetailKind);

public sealed record ChangeDetailDto(
    Guid Id,
    DateTimeOffset At,
    ChangeAction Action,
    string Target,
    string? UserName,
    string? ProgramId,
    ChangeDetailKind DetailKind,
    IReadOnlyList<string>? ConfigBullets,
    IReadOnlyList<ChangePointRowDto> Points);
```

```csharp
/// <summary>
/// Common report list filters (search + date range, paged) plus an optional per-tab category filter,
/// applied server-side so server-paginated lists filter the whole set, not just the current page. Each
/// category field carries the pt-BR wire literal of its enum (e.g. <c>status=Concluído</c>) and is
/// ignored if unrecognized. <c>To</c> is treated as the end of that day (inclusive).
/// </summary>
public sealed record ReportQuery(
    string? Search = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    int Page = 1,
    int PageSize = 10,
    string? Status = null,
    string? Action = null,
    string? Severity = null,
    string? Level = null,
    string? ProgramId = null);
```

### /home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Domain/Enums/Enums.cs

`RampShape` (lines 23-30), `ChangeAction` (lines 79-84), `ChangePointRole` (lines 163-174):

```csharp
/// <summary>Ramp shape between two setpoints in the profile editor (frontend <c>Ramp</c>).</summary>
public enum RampShape
{
    Linear,
    Fixo,
    [JsonStringEnumMemberName("Parábola positiva")] ParabolaPositiva,
    [JsonStringEnumMemberName("Parábola negativa")] ParabolaNegativa,
}
```

```csharp
public enum ChangeAction
{
    Criado,
    Editado,
    Removido,
}
```

```csharp
/// <summary>Role of a row in a program change-diff. An edit emits a per-point diff: a point that changed
/// appears twice (<c>changed-before</c> + <c>changed-after</c>, same index); a point present in only one
/// side is <c>added</c>/<c>removed</c>; a point with the same value on both sides is <c>unchanged</c>
/// (emitted once so the curve stays complete without being flagged as a change).</summary>
public enum ChangePointRole
{
    [JsonStringEnumMemberName("added")] Added,
    [JsonStringEnumMemberName("removed")] Removed,
    [JsonStringEnumMemberName("changed-before")] ChangedBefore,
    [JsonStringEnumMemberName("changed-after")] ChangedAfter,
    [JsonStringEnumMemberName("unchanged")] Unchanged,
}
```

### /home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Application/Services/ProgramService.cs

`BuildPoints` (lines 193-220) and `BuildEditDiff` (lines 222-268), plus the call sites that produce the `Points`:

```csharp
// CreateAsync, line 76:
        audit.RecordProgramChange(ChangeAction.Criado, program, BuildPoints(program, ChangePointRole.Added));

// UpdateAsync, lines 92-100:
        var before = BuildPoints(program, ChangePointRole.ChangedBefore);
        program.Name = name;
        program.Description = description;
        program.Segments = segments;
        program.Profile = profile;
        var after = BuildPoints(program, ChangePointRole.ChangedAfter);
        audit.RecordProgramChange(ChangeAction.Editado, program, BuildEditDiff(before, after));

// DeleteAsync, line 116:
        audit.RecordProgramChange(ChangeAction.Removido, program, BuildPoints(program, ChangePointRole.Removed));
```

```csharp
    private static List<ChangePointRow> BuildPoints(ReflowProgram p, ChangePointRole role)
    {
        var rows = new List<ChangePointRow>();
        if (p.Segments is { Count: > 0 })
        {
            var i = 1;
            var t = 0;
            foreach (var s in p.Segments.Take(DomainConstants.ProfileMaxPoints))
            {
                t += s.DurationSec;
                rows.Add(new ChangePointRow { Index = i++, Temp = s.Temp, TimeSec = t, Ramp = s.Ramp, Role = role });
            }
        }
        else
        {
            var i = 1;
            foreach (var pt in p.Profile.Take(DomainConstants.ProfileMaxPoints))
                rows.Add(new ChangePointRow
                {
                    Index = i++,
                    Temp = (int)Math.Round(pt.Temp),
                    TimeSec = (int)Math.Round(pt.T),
                    Ramp = RampShape.Linear,
                    Role = role,
                });
        }
        return rows;
    }

    /// <summary>
    /// Diff the old and new curves point-by-point (by position/index) into one role-tagged list:
    /// an unchanged point appears once (<see cref="ChangePointRole.Unchanged"/>); a changed point appears
    /// twice — <see cref="ChangePointRole.ChangedBefore"/> + <see cref="ChangePointRole.ChangedAfter"/> at
    /// the same index; a point only in the new curve is <see cref="ChangePointRole.Added"/>; one only in
    /// the old curve is <see cref="ChangePointRole.Removed"/>. The front rebuilds the <i>before</i> curve
    /// from removed+changed-before+unchanged and the <i>after</i> curve from added+changed-after+unchanged,
    /// and labels each point from its role. Position-based: inserting a point mid-curve shifts the rest, so
    /// the tail reads as changed — simple, predictable, and matches the index-keyed tables in the UI.
    /// </summary>
    private static List<ChangePointRow> BuildEditDiff(IReadOnlyList<ChangePointRow> before, IReadOnlyList<ChangePointRow> after)
    {
        var rows = new List<ChangePointRow>();
        var max = Math.Max(before.Count, after.Count);
        for (var i = 0; i < max; i++)
        {
            var b = i < before.Count ? before[i] : null;
            var a = i < after.Count ? after[i] : null;

            if (b is not null && a is not null)
            {
                if (b.Temp == a.Temp && b.TimeSec == a.TimeSec && b.Ramp == a.Ramp)
                {
                    a.Role = ChangePointRole.Unchanged;
                    rows.Add(a);
                }
                else
                {
                    b.Role = ChangePointRole.ChangedBefore;
                    a.Role = ChangePointRole.ChangedAfter;
                    rows.Add(b);
                    rows.Add(a);
                }
            }
            else if (a is not null)
            {
                a.Role = ChangePointRole.Added;
                rows.Add(a);
            }
            else
            {
                b!.Role = ChangePointRole.Removed;
                rows.Add(b);
            }
        }
        return rows;
    }
```

### /home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Domain/Entities/ChangeLogEntry.cs

`Points` jsonb field (line 27) and the `ChangePointRow` shape (lines 30-39):

```csharp
    /// <summary>Program change: the before/after/added/removed point diff. Stored as jsonb.</summary>
    public List<ChangePointRow> Points { get; set; } = new();
}

/// <summary>One row of a program change-diff. Owned/jsonb.</summary>
public class ChangePointRow
{
    /// <summary>1-based point index.</summary>
    public int Index { get; set; }
    public int Temp { get; set; }
    public int TimeSec { get; set; }
    public RampShape Ramp { get; set; }
    public ChangePointRole Role { get; set; }
}
```

### /home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Api/Controllers/ReportControllers.cs

`ChangesController` (lines 16-25) — the endpoints that bind `ReportQuery` and call the service unchanged (so adding `Before` to the record auto-binds from the query string with no controller edit):

```csharp
[ApiController]
[Route("api/changes")]
public sealed class ChangesController(ReportService reports) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<ChangeSummaryDto>> List([FromQuery] ReportQuery query, CancellationToken ct) => reports.ChangesAsync(query, ct);

    [HttpGet("{id:guid}")]
    public Task<ChangeDetailDto> Get(Guid id, CancellationToken ct) => reports.ChangeAsync(id, ct);
}
```

### /home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Infrastructure/Persistence/DbSeeder.Demo.cs (lines 239-279, the seeded diff shape `ChangeAsync` must consolidate)

```csharp
        List<ChangePointRow> Rows(ChangePointRole role, int tempDelta) =>
            [.. curve.Select((p, k) => new ChangePointRow
            {
                Index = k + 1,
                Temp = Math.Max(0, (int)Math.Round(p.Temp) + tempDelta),
                TimeSec = (int)Math.Round(p.T),
                Ramp = RampShape.Linear,
                Role = role,
            })];

        // Editado shows a real per-point diff ...
        List<ChangePointRow> EditDiff()
        {
            var diff = new List<ChangePointRow>();
            for (var k = 0; k < curve.Count; k++)
            {
                var t = (int)Math.Round(curve[k].T);
                var temp = (int)Math.Round(curve[k].Temp);
                if (k == 0)
                    diff.Add(new ChangePointRow { Index = k + 1, Temp = temp, TimeSec = t, Ramp = RampShape.Linear, Role = ChangePointRole.Unchanged });
                else if (k == curve.Count - 1)
                    diff.Add(new ChangePointRow { Index = k + 1, Temp = temp, TimeSec = t, Ramp = RampShape.Linear, Role = ChangePointRole.Removed });
                else
                {
                    diff.Add(new ChangePointRow { Index = k + 1, Temp = Math.Max(0, temp - 8), TimeSec = t, Ramp = RampShape.Linear, Role = ChangePointRole.ChangedBefore });
                    diff.Add(new ChangePointRow { Index = k + 1, Temp = temp, TimeSec = t, Ramp = RampShape.Linear, Role = ChangePointRole.ChangedAfter });
                }
            }
            var lastT = curve.Count > 0 ? (int)Math.Round(curve[^1].T) : 0;
            diff.Add(new ChangePointRow { Index = curve.Count + 1, Temp = 60, TimeSec = lastT + 30, Ramp = RampShape.Linear, Role = ChangePointRole.Added });
            return diff;
        }
```

Important structural note: the seeded `Editado` diff (and any real `BuildEditDiff` output) can have the SAME `Index` used by both a `ChangedBefore/ChangedAfter` pair AND a separate `Removed`/`Added`/`Unchanged` row is NOT the case there, but `Removed` at index `curve.Count` and `Added` at index `curve.Count+1` are distinct indices. Within one index, the only collision is the changed-before+changed-after pair. So consolidation can safely group by `Index`, treating the changed pair as one row.

## Change plan

1. **File `ReportDtos.cs` — add the new diff DTOs.** Insert immediately after the existing `ChangePointRowDto` line (line 79), keeping all in the `ReflowOven.Application.Dtos` namespace. Use `string` for `status` and `changedFields` to pin the exact lowercase wire literals (these are NOT existing enums and must not be confused with `ChangePointRole`):

   ```csharp
   /// <summary>One side of a per-point diff (before or after edit). Null when the point exists on only one side.</summary>
   public sealed record ChangePointValueDto(int Temp, int TimeSec, RampShape Ramp);

   /// <summary>Consolidated per-point diff row: one row per index. <c>Status</c> is
   /// <c>unchanged|changed|added|removed</c>; <c>ChangedFields</c> lists which of
   /// <c>temp|timeSec|ramp</c> differ (only for status=changed).</summary>
   public sealed record ChangePointDiffDto(
       int Index,
       string Status,
       ChangePointValueDto? Before,
       ChangePointValueDto? After,
       IReadOnlyList<string> ChangedFields);

   public sealed record ChangeDiffSummaryDto(
       int Total,
       int Unchanged,
       int Changed,
       int Added,
       int Removed);

   public sealed record ChangeDiffDto(
       ChangeDiffSummaryDto Summary,
       IReadOnlyList<ChangePointDiffDto> Points,
       IReadOnlyList<ChangePointValueDto> BeforeCurve,
       IReadOnlyList<ChangePointValueDto> AfterCurve);
   ```

   Then **extend `ChangeDetailDto`** (lines 89-98) to carry the consolidated diff. Append a nullable field so config changes (no points) stay `null`:
   - old last param: `IReadOnlyList<ChangePointRowDto> Points);`
   - new: `IReadOnlyList<ChangePointRowDto> Points,` + new line `ChangeDiffDto? Diff = null);`
   (Keeping `Points` preserves backward compatibility; the front can migrate to `Diff`. If the orchestrator prefers replacing the role-rows entirely, drop `Points`, but that is a breaking front change — recommend keeping both during transition.)

2. **File `ReportDtos.cs` — add `Before` to `ReportQuery`.** In the `ReportQuery` record (lines 111-121), add a new optional parameter. Place it after `To` to keep the date filters together; because all params have defaults and the controller binds `[FromQuery]` by name, ordering does not break callers:
   - old: `DateTimeOffset? To = null,`
   - new: `DateTimeOffset? To = null,` followed immediately by `DateTimeOffset? Before = null,`
   No controller edit needed — `ChangesController.List` already binds `[FromQuery] ReportQuery`, so `?before=2026-05-30T12:00:00Z` auto-binds.

3. **File `ReportService.cs` — apply `Before` (strictly-earlier, exclusive) in `ChangesAsync`.** In `ChangesAsync` (lines 79-89), after the `ToExclusive`/`From` filters and before `CountAsync` (line 90), add:
   - insert after line 86 (`if (ToExclusive(q) is { } toExc) ...`):
     ```csharp
     if (q.Before is not null) query = query.Where(c => c.At < q.Before);
     ```
   This is applied before `CountAsync` and before `Skip/Take`, so `total` and paging both honor the cursor. `From`/`To` and `Before` compose (all are `Where` clauses).

4. **File `ReportService.cs` — build the consolidated diff in `ChangeAsync`.** Replace the `ChangeAsync` body (lines 100-108). Read the role rows once, build the existing `Points` list as today, then call a new private helper to consolidate. For config changes (`DetailKind.Config`, `Points` empty), pass `Diff = null`:

   ```csharp
   public async Task<ChangeDetailDto> ChangeAsync(Guid id, CancellationToken ct = default)
   {
       var c = await db.Changes.FirstOrDefaultAsync(x => x.Id == id, ct)
           ?? throw new NotFoundException("Alteração não encontrada.");
       var rows = c.Points.OrderBy(p => p.Index)
           .Select(p => new ChangePointRowDto(p.Index, p.Temp, p.TimeSec, p.Ramp, p.Role)).ToList();
       var diff = rows.Count > 0 ? BuildChangeDiff(rows) : null;
       return new ChangeDetailDto(
           c.Id, c.At, c.Action, c.Target, c.UserName, c.ProgramId, c.DetailKind,
           c.ConfigBullets, rows, diff);
   }
   ```

5. **File `ReportService.cs` — add the `BuildChangeDiff` consolidation helper.** Add a private static method (near the other helpers, after `MapSnapshot`, before the closing brace at line 150). It groups the stored role rows by `Index`, collapsing the `ChangedBefore`+`ChangedAfter` pair into one `changed` row, maps `Added`/`Removed`/`Unchanged` straight through, computes `changedFields`, derives summary counts, and builds the before/after curves:

   ```csharp
   private static ChangeDiffDto BuildChangeDiff(IReadOnlyList<ChangePointRowDto> rows)
   {
       var points = new List<ChangePointDiffDto>();
       foreach (var g in rows.GroupBy(r => r.Index).OrderBy(g => g.Key))
       {
           var bRow = g.FirstOrDefault(r => r.Role is ChangePointRole.ChangedBefore or ChangePointRole.Removed or ChangePointRole.Unchanged);
           var aRow = g.FirstOrDefault(r => r.Role is ChangePointRole.ChangedAfter or ChangePointRole.Added or ChangePointRole.Unchanged);

           var before = bRow is null ? null : new ChangePointValueDto(bRow.Temp, bRow.TimeSec, bRow.Ramp);
           var after  = aRow is null ? null : new ChangePointValueDto(aRow.Temp, aRow.TimeSec, aRow.Ramp);

           string status;
           if (before is not null && after is not null)
               status = g.Any(r => r.Role is ChangePointRole.ChangedBefore or ChangePointRole.ChangedAfter) ? "changed" : "unchanged";
           else if (after is not null) status = "added";
           else status = "removed";

           var fields = new List<string>();
           if (status == "changed")
           {
               if (before!.Temp    != after!.Temp)    fields.Add("temp");
               if (before.TimeSec  != after.TimeSec)  fields.Add("timeSec");
               if (before.Ramp     != after.Ramp)     fields.Add("ramp");
           }

           points.Add(new ChangePointDiffDto(g.Key, status, before, after, fields));
       }

       var summary = new ChangeDiffSummaryDto(
           points.Count,
           points.Count(p => p.Status == "unchanged"),
           points.Count(p => p.Status == "changed"),
           points.Count(p => p.Status == "added"),
           points.Count(p => p.Status == "removed"));

       var beforeCurve = points.Where(p => p.Before is not null).Select(p => p.Before!).ToList();
       var afterCurve  = points.Where(p => p.After  is not null).Select(p => p.After!).ToList();

       return new ChangeDiffDto(summary, points, beforeCurve, afterCurve);
   }
   ```

   Notes for the implementer:
   - `Unchanged` rows are stored once (single row, role=Unchanged) but represent both sides → mapped into both `before` and `after`. The `FirstOrDefault` selectors above intentionally include `Unchanged` in BOTH the before-selector and after-selector so an unchanged point produces non-null before AND after, yielding `status="unchanged"` and contributing to both curves.
   - For `Criado` (all rows role=Added) every point is `added` → empty before-curve, full after-curve. For `Removido` (all role=Removed) → full before-curve, empty after-curve. This matches the `BuildPoints`/seed shape.
   - `changedFields` only computed for `changed`; `added`/`removed`/`unchanged` get an empty list. The plan said the literals are exactly `temp|timeSec|ramp` — matched.

6. **File `ReportService.cs` — confirm `ChangePointRole` stays internal to the consolidation.** The new helper references `ChangePointRole.*` (Domain enum, already globally imported). The new wire DTOs (`ChangePointDiffDto`) expose `Status`/`ChangedFields` as plain strings, so `ChangePointRole` is never serialized through the new diff. The legacy `Points` list still serializes `ChangePointRole` (unchanged behavior). This satisfies "Keep ChangePointRole internal" for the new shape while not breaking the existing field.

7. **No change required** in `ProgramService.cs`, `ChangeLogEntry.cs`, `DbSeeder.Demo.cs`, `ReportControllers.cs`, or `Enums.cs`. The diff is computed at read time from the already-persisted role rows; producers (BuildEditDiff/BuildPoints/seed) are untouched.

## Migration?

**no.** This is read-time only. No new persisted columns, tables, or entity changes. `ChangeLogEntry.Points` (jsonb via `OwnsMany(...).ToJson()`) and `ChangePointRow` are unchanged — the consolidation runs in `ReportService.ChangeAsync` over the already-loaded `Points`. The new `Before` filter on `ReportQuery` is a query parameter, not a column. The `PtBrEnumConverter<ChangePointRole>` mapping and the jsonb column stay exactly as they are, so there is no `CHECK`/enum constraint to alter. (Adding `Diff`/`Before` to existing DTO records is a code-only / OpenAPI-doc change.)

## Contract/enum notes

- **Do NOT reuse existing enums for the new `status`/`changedFields`.** The plan specifies literal strings `unchanged|changed|added|removed` and `temp|timeSec|ramp`. Implement these as plain `string` on the DTOs (as in step 1/5), NOT as `ChangePointRole` (whose wire literals are `added`/`removed`/`changed-before`/`changed-after`/`unchanged` — note `changed-before`/`changed-after`, which differ from the consolidated `changed`). Keeping them as strings avoids minting a new pt-BR-attributed enum and keeps `ChangePointRole` out of the new shape.
- **`ChangePointRole` `[JsonStringEnumMemberName]` literals must stay exact** if `ChangeDetailDto.Points` (the legacy role-rows list) is retained: `added`, `removed`, `changed-before`, `changed-after`, `unchanged`. The frontend currently rebuilds curves from these (per the `BuildEditDiff` doc comment). Recommend keeping `Points` in the DTO during transition so the front does not break; the new `Diff` block is additive.
- **`RampShape` literals are contract:** `Linear`, `Fixo`, `Parábola positiva`, `Parábola negativa`. The new `ChangePointValueDto.Ramp` serializes via the same global `JsonStringEnumConverter`, so it emits these exact pt-BR strings — keep the type as `RampShape`, do not stringify it manually.
- **`ChangeDetailDto` shape the front depends on:** existing fields (Id, At, Action, Target, UserName, ProgramId, DetailKind, ConfigBullets, Points) must remain in place and order if `Diff` is appended as the last optional param (`ChangeDiffDto? Diff = null`). Appending a trailing optional is non-breaking for JSON deserializers that match by name.
- **`ReportQuery.Before` binds by name** from `?before=...` (ISO 8601). It composes with existing `from`/`to`; semantics are strictly-earlier, EXCLUSIVE (`c.At < before`), distinct from `to` which is inclusive-end-of-day via `ToExclusive`.

## Risks / open questions

- **Index collision across roles.** Consolidation groups by `Index`. In current producers, the only same-index duplication is the changed-before/changed-after pair; `added`/`removed`/`unchanged` rows have distinct indices. The `GroupBy(Index)` + role-aware before/after selection handles the seed and `BuildEditDiff` output correctly. If a future producer ever emits, say, a `Removed` and an `Added` at the same `Index`, the grouping would merge them into a single `changed` row (before from Removed, after from Added) — verify that is acceptable, or key the grouping more strictly. Current code does not do this, so it is safe today.
- **Whether to keep or drop the legacy `Points` list.** Keeping it is backward-compatible but duplicates data on the wire; dropping it is cleaner but a breaking front change. Recommendation: keep both, deprecate `Points` later. Needs a frontend-coordination decision (the front's Alterações detail screen currently consumes `Points`).
- **Unchanged-row dual-mapping assumption.** The design relies on a single `Unchanged` row representing both sides (so it lands in both before/after curves). This matches `BuildEditDiff` (emits Unchanged once) and the seed. If any path emitted unchanged as a before/after pair, the group selectors would still work, but `status` detection (`Any(ChangedBefore/ChangedAfter)`) would correctly stay `unchanged`. Low risk.
- **Empty / config diffs.** Config changes have empty `Points` → `Diff = null`. Confirm the front treats `diff: null` for config rows (it already special-cases `ConfigBullets` vs program points via `DetailKind`).
- **Curve ordering.** `beforeCurve`/`afterCurve` are derived in `Index` order from the consolidated points. This preserves point order but, per the `BuildEditDiff` doc note, mid-curve inserts shift the tail (read as changed) — that pre-existing position-based behavior carries into the curves unchanged; not a regression.
- **No new tests referenced.** There is likely a `ProfileBuilderTests`/report test area; adding a `ChangeAsync` consolidation unit test (changed/added/removed/unchanged mix + changedFields + summary + before/after curves) is advisable but outside the read-only scope here.