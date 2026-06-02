# itemJ-seed-edited-program

I now have everything. Apologies for the redundant tool calls earlier — the sandbox was intermittently returning empty output. I have full, verified copies of all relevant code.

## Files

### /home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Infrastructure/Persistence/DbSeeder.cs
Entry point `SeedAsync` (base seed). It seeds the factory + catalog programs only when the Programs table is empty (`IgnoreQueryFilters`). Demo seeding is a **separate** partial-class method (`SeedDemoAsync`, in DbSeeder.Demo.cs), called from `Program.cs` only when `Seed:Demo` is true.

```csharp
// full file (51 lines)
namespace ReflowOven.Infrastructure.Persistence;

/// <summary>Idempotent first-run seeding (called after Database.Migrate). Mirrors the frontend defaults.</summary>
public static partial class DbSeeder
{
    public static async Task SeedAsync(ReflowDbContext db, IPasswordHasher hasher, IClock clock, CancellationToken ct = default)
    {
        if (!await db.FaultTypes.AnyAsync(ct))
            db.FaultTypes.AddRange(Defaults.FaultTypes());
        // ... settings / calibration / deviceinfo+boards ...
        if (!await db.Programs.IgnoreQueryFilters().AnyAsync(ct))
        {
            db.Programs.Add(Defaults.FactoryProgram());
            db.Programs.AddRange(Defaults.CatalogPrograms());
        }
        // ... users ...
        await db.SaveChangesAsync(ct);
    }
}
```

How it is wired (Program.cs lines 162-165):
```csharp
await ReflowOven.Api.StartupDiagnostics.MigrateAndLogAsync(db, app.Logger);
await DbSeeder.SeedAsync(db, sp.GetRequiredService<IPasswordHasher>(), clock);
if (app.Configuration.GetValue<bool>("Seed:Demo"))
    await DbSeeder.SeedDemoAsync(db, clock);
```

### /home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Infrastructure/Persistence/DbSeeder.Demo.cs
Entry point `SeedDemoAsync(db, clock)`. **Idempotency guards are per-table** (`if (!await db.Changes.AnyAsync(ct))`), and there is a single `SaveChangesAsync` at the end. The change rows are built by `BuildChange`.

```csharp
// lines 37-86 — SeedDemoAsync
public static async Task SeedDemoAsync(ReflowDbContext db, IClock clock, CancellationToken ct = default)
{
    var now = clock.UtcNow;
    var users = await db.Users.OrderBy(u => u.CreatedAt).ToListAsync(ct);
    var programs = await db.Programs.OrderBy(p => p.Id).ToListAsync(ct);
    var faults = await db.FaultTypes.OrderBy(f => f.Code).ToListAsync(ct);
    if (users.Count == 0 || programs.Count == 0) return;

    // ... login counts, activity stats, favorites, executions, errors ...

    if (!await db.Changes.AnyAsync(ct))
        for (var i = 0; i < 16; i++)
            db.Changes.Add(BuildChange(i, now, programs, users));

    // ... system log, board hours ...

    await db.SaveChangesAsync(ct);
}
```

```csharp
// lines 217-293 — BuildChange (current per-point diff builder; verbatim)
private static ChangeLogEntry BuildChange(int i, DateTimeOffset now, List<ReflowProgram> programs, List<User> users)
{
    var user = users[i % users.Count];
    var at = now.AddDays(-(i + 1)).AddHours(-(i % 7));

    if (i % 4 == 1)
        return new ChangeLogEntry
        {
            Id = Guid.NewGuid(),
            At = at,
            Action = ChangeAction.Editado,
            Target = "Configuração do sistema",
            UserId = user.Id,
            UserName = user.Name,
            DetailKind = ChangeDetailKind.Config,
            ConfigBullets = [.. ConfigBulletSets[i % ConfigBulletSets.Length]],
        };

    var action = (ChangeAction)(i % 3); // Criado | Editado | Removido
    var program = programs[i % programs.Count];
    var curve = program.Profile.Skip(1).Take(8).ToList();

    List<ChangePointRow> Rows(ChangePointRole role, int tempDelta) =>
        [.. curve.Select((p, k) => new ChangePointRow
        {
            Index = k + 1,
            Temp = Math.Max(0, (int)Math.Round(p.Temp) + tempDelta),
            TimeSec = (int)Math.Round(p.T),
            Ramp = RampShape.Linear,
            Role = role,
        })];

    // Editado shows a real per-point diff so the Alterações screen exercises every role: point 1
    // unchanged, the middle points changed (previous ~8 °C cooler), the last removed, plus one
    // appended (added). Criado/Removido carry the single added/removed curve.
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

    var points = action switch
    {
        ChangeAction.Criado => Rows(ChangePointRole.Added, 0),
        ChangeAction.Removido => Rows(ChangePointRole.Removed, 0),
        _ => EditDiff(),
    };

    return new ChangeLogEntry
    {
        Id = Guid.NewGuid(),
        At = at,
        Action = action,
        Target = program.Name,
        UserId = user.Id,
        UserName = user.Name,
        ProgramId = program.Id,
        DetailKind = ChangeDetailKind.Program,
        Points = points,
    };
}
```

```csharp
// lines 295-305 — the deterministic Hash helper (FNV-1a → non-negative int)
private static int Hash(string s)
{
    uint h = 2166136261;
    foreach (var c in s) { h ^= c; h *= 16777619; }
    return (int)(h & 0x7fffffff);
}
```

Note: the existing `BuildChange` spreads its 16 rows across **different** programs (`programs[i % programs.Count]`), so no single program gets 12 program-change rows — the retention cutoff is never exercised. There is no helper in DbSeeder.Demo.cs that creates a `ReflowProgram`; programs are pulled from the already-seeded set. (The only `ReflowProgram` factory is `Defaults.Catalog(...)`.)

### /home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Domain/Entities/ChangeLogEntry.cs
```csharp
public class ChangeLogEntry
{
    public Guid Id { get; set; }
    public DateTimeOffset At { get; set; }
    public ChangeAction Action { get; set; }
    public string Target { get; set; } = "";          // program name, or "Configuração do sistema"
    public Guid? UserId { get; set; }
    public string? UserName { get; set; }
    public string? ProgramId { get; set; }
    public ChangeDetailKind DetailKind { get; set; }
    public List<string>? ConfigBullets { get; set; }   // jsonb, null for program changes
    public List<ChangePointRow> Points { get; set; } = new();  // jsonb
}

public class ChangePointRow
{
    public int Index { get; set; }   // 1-based
    public int Temp { get; set; }
    public int TimeSec { get; set; }
    public RampShape Ramp { get; set; }
    public ChangePointRole Role { get; set; }
}
```

### /home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Domain/Entities/ReflowProgram.cs
```csharp
public class ReflowProgram
{
    public string Id { get; set; } = "";              // slug for seeds OR Guid string for user-created
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public int RunCount { get; set; }
    public DateTimeOffset? LastUsed { get; set; }      // null = "Nunca"
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public bool IsSeed { get; set; }                   // built-in catalog (hidden, not removed)
    public List<ProfilePoint> Profile { get; set; } = new();   // jsonb; starts t=0, temp=25
    public List<ProfileSegment>? Segments { get; set; }        // jsonb; absent on seed programs
}
public class ProfilePoint { public double T { get; set; } public double Temp { get; set; } }
public class ProfileSegment { public int Temp; public int DurationSec; public RampShape Ramp = RampShape.Linear; }
```
A global query filter hides `IsDeleted` programs (DbContext line 74).

### /home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Application/Services/ProgramService.cs
Real edits build the diff and **prune on write**. `UpdateAsync` (lines 83-107): snapshots `before`, mutates the program, snapshots `after`, calls `audit.RecordProgramChange(ChangeAction.Editado, program, BuildEditDiff(before, after))` then `await audit.PruneProgramChangesAsync(program.Id, ct)`, then a single `SaveChangesAsync`. `CreateAsync` records `Criado` + `BuildPoints(..., Added)`; `DeleteAsync` records `Removido` + `BuildPoints(..., Removed)`. All three prune after recording.

`BuildEditDiff` (lines 232-268) is the canonical role logic the demo should mirror: equal point → one `Unchanged` row; differing point → two rows `ChangedBefore` + `ChangedAfter` at the same index; extra new point → `Added`; extra old point → `Removed`.

### /home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Application/Services/AuditService.cs
The retention trim is **write-time only** — there is no read-side cap:
```csharp
// lines 48-56
public async Task PruneProgramChangesAsync(string programId, CancellationToken ct = default)
{
    var stale = await db.Changes
        .Where(c => c.ProgramId == programId && c.DetailKind == ChangeDetailKind.Program)
        .OrderByDescending(c => c.At)
        .Skip(DomainConstants.ChangeRetentionPerProgramMax - 1)   // keep cap-1 persisted + 1 just-staged = cap
        .ToListAsync(ct);
    if (stale.Count > 0) db.Changes.RemoveRange(stale);
}
```
Key detail: it is meant to be called **after** staging exactly one new (unsaved) row, so it keeps `cap-1` persisted rows. Ordering is by `At` (descending) — so for the cutoff to be faithful, timestamps must be strictly monotonic.

### /home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Domain/Common/DomainConstants.cs
```csharp
public const int ProfileMaxPoints = 100;                 // line 17
public const int ChangeRetentionPerProgramMax = 10;      // line 89 — most-recent kept; older pruned on next change
```

### /home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Application/Common/Defaults.cs
The `Catalog` factory (lines 195-203) is the reusable `ReflowProgram` builder + point shape. The factory program (lines 141-143):
```csharp
public static ReflowProgram FactoryProgram() => Catalog(
    FactoryProgramId, "Perfil Padrão", 0, null,
    [(0, 25), (90, 150), (180, 180), (210, 217), (240, 245), (270, 210), (330, 120), (390, 45)]);

private static ReflowProgram Catalog(string id, string name, int runCount, string? lastUsed, (double t, double temp)[] pts) => new()
{
    Id = id,
    Name = name,
    RunCount = runCount,
    LastUsed = ParseDate(lastUsed),
    IsSeed = true,
    Profile = pts.Select(p => new ProfilePoint { T = p.t, Temp = p.temp }).ToList(),
};
```

### Enum wire literals (/home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Domain/Enums/Enums.cs)
```csharp
public enum ChangeAction { Criado, Editado, Removido }   // NO attribute — wire = identifier text
public enum ChangeDetailKind {
    [JsonStringEnumMemberName("config")] Config,
    [JsonStringEnumMemberName("program")] Program,
}
public enum ChangePointRole {
    [JsonStringEnumMemberName("added")] Added,
    [JsonStringEnumMemberName("removed")] Removed,
    [JsonStringEnumMemberName("changed-before")] ChangedBefore,
    [JsonStringEnumMemberName("changed-after")] ChangedAfter,
    [JsonStringEnumMemberName("unchanged")] Unchanged,
}
public enum RampShape {
    Linear, Fixo,
    [JsonStringEnumMemberName("Parábola positiva")] ParabolaPositiva,
    [JsonStringEnumMemberName("Parábola negativa")] ParabolaNegativa,
}
```

### DbContext mapping (/home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Infrastructure/Persistence/ReflowDbContext.cs)
`Changes` DbSet line 23; `ChangeLogEntry` entity lines 135-143: `Points` is `OwnsMany(...).ToJson()` (jsonb), `Target` max 120, `UserName` max 40, `ProgramId` max 64, index on `At`. (`ConfigBullets` is a plain `List<string>?` → jsonb by Npgsql convention; not explicitly configured.) No CHECK constraint on this table.

## Change plan

**Retention is enforced on write only**, never at read. So a naive `db.Changes.AddRange(12 rows)` followed by one `SaveChangesAsync` will persist all 12 — the report would show 12, not 10. To faithfully demonstrate the cutoff at 10, the seed must **emulate the write-time prune**: build 12 rows, then keep only the most-recent `ChangeRetentionPerProgramMax` (10) by `At` before adding them, so exactly 10 are persisted and the 2 oldest (the `Criado` + the first `Editado`) are dropped — visibly demonstrating the cutoff. (Alternatively persist all 12 then delete the oldest 2; keeping-before-add is simpler and matches the "demo data" idempotency model.)

All edits are in **one file**: `/home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Infrastructure/Persistence/DbSeeder.Demo.cs`. No changes to ProgramService/AuditService/Defaults/DbContext are required.

1. **DbSeeder.Demo.cs — add a constant for the demo program.** Near the top of the class (after `private const int SnapshotSamples = 40;`, line 11), add:
   ```csharp
   private const string EditChurnProgramId = "demo-edit-churn";
   ```

2. **DbSeeder.Demo.cs — `SeedDemoAsync`, inside the `if (!await db.Changes.AnyAsync(ct))` block (lines 70-72).** This `Changes`-empty guard already provides idempotency for the new rows too, so keep them inside it. After the existing loop, append the new churn seed. Replace:
   ```csharp
   if (!await db.Changes.AnyAsync(ct))
       for (var i = 0; i < 16; i++)
           db.Changes.Add(BuildChange(i, now, programs, users));
   ```
   with:
   ```csharp
   if (!await db.Changes.AnyAsync(ct))
   {
       for (var i = 0; i < 16; i++)
           db.Changes.Add(BuildChange(i, now, programs, users));

       // Item J: one program edited ~12 times so the Alterações report shows the
       // ChangeRetentionPerProgramMax (10) cutoff. Retention is enforced on WRITE only
       // (AuditService.PruneProgramChangesAsync), so emulate it here: keep only the most
       // recent N rows by timestamp, dropping the 2 oldest (Criado + first Editado).
       var churnProgram = await SeedEditChurnProgramAsync(db, ct);
       var churn = BuildEditChurnHistory(churnProgram, now, users);
       foreach (var c in churn
                    .OrderByDescending(c => c.At)
                    .Take(DomainConstants.ChangeRetentionPerProgramMax))
           db.Changes.Add(c);
   }
   ```
   Idempotency note: this is gated by the same `!db.Changes.AnyAsync` guard, so it never double-seeds. The new program is created idempotently in step 3 (guarded by an `IgnoreQueryFilters().AnyAsync(Id ==)` check), so re-running `SeedDemoAsync` against a DB that already has Changes does nothing.

3. **DbSeeder.Demo.cs — add the program-creator helper** (mirrors `Defaults.Catalog`, but as a user-style program so it is editable/visible — `IsSeed = false`, not soft-deleted). Add after `BuildChange` (after line 293):
   ```csharp
   /// <summary>Idempotently create the one program whose edit history demonstrates the retention cutoff.</summary>
   private static async Task<ReflowProgram> SeedEditChurnProgramAsync(ReflowDbContext db, CancellationToken ct)
   {
       var existing = await db.Programs.IgnoreQueryFilters()
           .FirstOrDefaultAsync(p => p.Id == EditChurnProgramId, ct);
       if (existing is not null) return existing;

       var program = new ReflowProgram
       {
           Id = EditChurnProgramId,
           Name = "Perfil Teste de Edições",
           Description = "Editado várias vezes (demo da retenção do histórico).",
           RunCount = 0,
           LastUsed = null,
           IsSeed = false,
           Profile =
           [
               new() { T = 0, Temp = 25 },
               new() { T = 90, Temp = 150 },
               new() { T = 180, Temp = 180 },
               new() { T = 210, Temp = 217 },
               new() { T = 240, Temp = 245 },
               new() { T = 270, Temp = 210 },
               new() { T = 330, Temp = 120 },
               new() { T = 390, Temp = 45 },
           ],
       };
       db.Programs.Add(program);
       return program;
   }
   ```
   Note: `ReflowProgram.Id` column is max 64 — `"demo-edit-churn"` fits. `IgnoreQueryFilters()` is required because a future re-run could otherwise miss a row hidden by the soft-delete filter.

4. **DbSeeder.Demo.cs — add the 12-row history builder** (1 `Criado` + 11 `Editado`, strictly increasing `At` so the prune ordering is deterministic, realistic per-point diffs reusing the existing role pattern). Add after the helper from step 3:
   ```csharp
   /// <summary>
   /// 12 program-change rows for one program: 1 Criado + 11 Editado, oldest→newest, each Editado
   /// nudging two mid-curve points warmer so the per-point diff exercises ChangedBefore/ChangedAfter
   /// alongside Unchanged. Timestamps strictly increase so the most-recent-N retention cut is exact.
   /// </summary>
   private static List<ChangeLogEntry> BuildEditChurnHistory(ReflowProgram program, DateTimeOffset now, List<User> users)
   {
       var rows = new List<ChangeLogEntry>();
       var basePts = program.Profile.Skip(1).Take(8).ToList(); // skip the t=0/25 anchor
       const int edits = 11;

       // Each row is older the larger its index; row 0 (Criado) is the oldest.
       DateTimeOffset At(int rev) => now.AddDays(-30).AddHours(rev * 6);

       ChangeLogEntry Row(int rev, ChangeAction action, List<ChangePointRow> points) => new()
       {
           Id = Guid.NewGuid(),
           At = At(rev),
           Action = action,
           Target = program.Name,
           UserId = users[rev % users.Count].Id,
           UserName = users[rev % users.Count].Name,
           ProgramId = program.Id,
           DetailKind = ChangeDetailKind.Program,
           Points = points,
       };

       // rev 0: Criado — the whole curve as Added.
       rows.Add(Row(0, ChangeAction.Criado,
           [.. basePts.Select((p, k) => new ChangePointRow
           {
               Index = k + 1,
               Temp = (int)Math.Round(p.Temp),
               TimeSec = (int)Math.Round(p.T),
               Ramp = RampShape.Linear,
               Role = ChangePointRole.Added,
           })]));

       // rev 1..11: Editado — bump points 4 & 5 (peak region) by +rev °C; the rest stay Unchanged.
       for (var rev = 1; rev <= edits; rev++)
       {
           var diff = new List<ChangePointRow>();
           for (var k = 0; k < basePts.Count; k++)
           {
               var t = (int)Math.Round(basePts[k].T);
               var temp = (int)Math.Round(basePts[k].Temp);
               var changed = k == 3 || k == 4;
               if (changed)
               {
                   diff.Add(new ChangePointRow { Index = k + 1, Temp = temp, TimeSec = t, Ramp = RampShape.Linear, Role = ChangePointRole.ChangedBefore });
                   diff.Add(new ChangePointRow { Index = k + 1, Temp = temp + rev, TimeSec = t, Ramp = RampShape.Linear, Role = ChangePointRole.ChangedAfter });
               }
               else
               {
                   diff.Add(new ChangePointRow { Index = k + 1, Temp = temp, TimeSec = t, Ramp = RampShape.Linear, Role = ChangePointRole.Unchanged });
               }
           }
           rows.Add(Row(rev, ChangeAction.Editado, diff));
       }
       return rows;
   }
   ```
   With `At(rev) = now-30d + rev*6h`, rev 0 (Criado) is oldest and rev 11 newest; `OrderByDescending(At).Take(10)` in step 2 keeps rev 11..2 and drops rev 0 (Criado) + rev 1 — exactly the 10-row cutoff, visibly missing the creation row.

5. **(Optional) Favorites/executions interplay — none required.** The churn program is `IsSeed = false` and not deleted, so it appears normally in the Programas gallery and Alterações report. No change to the favorites loop (it `Take(6)` from the ordered seed set) is needed.

No `Program.cs`, `Defaults.cs`, enum, DTO, or DbContext edits.

## Migration?

**No.** The plan adds only data rows (`ReflowProgram` + `ChangeLogEntry`) and a couple of `private const`/helper methods inside `DbSeeder.Demo.cs`. No new entity, no new column, no schema change. `ChangeLogEntry.Points` is already mapped as jsonb via `OwnsMany(...).ToJson()` (DbContext line 141) and `ChangeAction`/`ChangePointRole`/`RampShape`/`ChangeDetailKind` already have value converters. There is **no CHECK or enum DB constraint on the Changes table** that could block the data (the only CHECKs are `CK_Users_Name` and the single-row `Id=1` constraints on Settings/Calibration/DeviceInfo). `ReflowProgram.Id` is `varchar(64)`; `"demo-edit-churn"` and `Name = "Perfil Teste de Edições"` (≤40) fit the configured max-lengths.

## Contract/enum notes
- `ChangeAction` has **no** `[JsonStringEnumMemberName]` — the wire/DB literals are the C# identifiers verbatim: `Criado`, `Editado`, `Removido`. Use these spellings exactly.
- `ChangeDetailKind.Program` serializes to `"program"`; `.Config` to `"config"`. Program changes must set `DetailKind = ChangeDetailKind.Program` (and carry `ProgramId`) — `PruneProgramChangesAsync` filters on `DetailKind == Program`, and the frontend Alterações tab routes the diff renderer off this.
- `ChangePointRole` wire literals are hyphenated/lowercase: `added`, `removed`, `changed-before`, `changed-after`, `unchanged`. Front rebuilds the *before* curve from removed+changed-before+unchanged and the *after* curve from added+changed-after+unchanged; a changed point must emit BOTH `ChangedBefore` and `ChangedAfter` at the **same `Index`** (the demo does this).
- `RampShape.Linear` is fine; the accented variants (`Parábola positiva/negativa`) and `Fixo` are the contract literals if ever used.
- The DTO the front consumes for a change row carries `at` as **ISO 8601** (the frontend maps to epoch-ms), plus `action`, `target`, `userName`, `detailKind`, and `points[]` of `{index, temp, timeSec, ramp, role}`. No DTO change is needed — the seed only writes entities that the existing ReportService → DTO mapping already handles.

## Risks / open questions
- **Faithful cutoff vs. naive seed.** If the 12 rows are added without the `OrderByDescending(At).Take(10)` trim, the report will show 12 rows for this program and NOT demonstrate the cutoff (retention is write-only). The plan trims in the seed to mimic the real prune. Confirm the intent is "show the post-trim state (10 rows, creation dropped)" rather than "12 rows present." If instead you want the report to literally show retention *happening* over time, the only faithful alternative is to drive 12 real `ProgramService.UpdateAsync` calls — heavier and out of scope for DbSeeder.
- **Idempotency coupling.** The new rows live under the existing `!db.Changes.AnyAsync` guard, so on a DB that already has *any* change rows (e.g. after the existing 16-row demo seed ran once) the churn rows won't be added. That's correct for "don't double-seed," but means you cannot add item-J rows to an already-demo-seeded DB without a fresh DB (or a factory reset). If you want item J to seed independently, gate it on its own check, e.g. `if (!await db.Changes.AnyAsync(c => c.ProgramId == EditChurnProgramId, ct))` and create the program separately — flag if that independence is desired.
- **`MaintenanceService.FactoryResetAsync`** rebuilds from `Defaults` (not from DbSeeder.Demo), so the churn program/history is correctly **not** recreated by a factory reset — consistent with "demo-only" data. Confirm that is intended.
- **Program visibility.** `IsSeed = false` puts "Perfil Teste de Edições" in the normal user gallery (and it can be edited/deleted by users, which would then append a real change row and trigger the real prune). If you'd rather hide it from the gallery, set `IsSeed = true` (it still shows in Alterações via `Target`/`ProgramId`); decide which you want.
- **`ChangeAction action = (ChangeAction)(i % 3)`** in the existing `BuildChange` relies on enum declaration order (`Criado=0, Editado=1, Removido=2`); the new code uses the named members instead, so it's robust to reordering. No conflict.
- Minor: the churn program's profile duplicates the factory curve; if you want it visually distinct in the report, give it different temps. Not required.