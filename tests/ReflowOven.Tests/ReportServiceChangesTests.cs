using Microsoft.EntityFrameworkCore;
using ReflowOven.Application.Common;
using ReflowOven.Application.Dtos;
using ReflowOven.Application.Services;
using ReflowOven.Domain.Entities;
using ReflowOven.Domain.Enums;
using ReflowOven.Infrastructure.Persistence;
using Xunit;

namespace ReflowOven.Tests;

/// <summary>
/// Covers the per-program change history filter: GET /api/changes?programId=... must return only that
/// program's change rows (so the editor can show a single program's edit history, naturally bounded by
/// paging + the per-program retention cap). Runs against the EF Core InMemory provider.
/// </summary>
public sealed class ReportServiceChangesTests
{
    [Fact]
    public async Task ChangesAsync_filters_by_programId()
    {
        await using var db = NewContext();
        db.Changes.AddRange(
            ProgramChange("p1", "Programa 1", 1),
            ProgramChange("p1", "Programa 1", 2),
            ProgramChange("p2", "Programa 2", 3),
            ConfigChange(4));
        await db.SaveChangesAsync();

        var service = new ReportService(db);

        var onlyP1 = await service.ChangesAsync(new ReportQuery(ProgramId: "p1"));
        Assert.Equal(2, onlyP1.Total);
        Assert.All(onlyP1.Items, c => Assert.Equal("Programa 1", c.Target));

        // No filter still returns everything (the two p1 rows, p2, and the config change).
        var all = await service.ChangesAsync(new ReportQuery());
        Assert.Equal(4, all.Total);
    }

    [Fact]
    public async Task ChangesAsync_before_cursor_excludes_newer_rows()
    {
        await using var db = NewContext();
        var cutoff = new DateTimeOffset(2026, 5, 31, 12, 0, 0, TimeSpan.Zero);
        db.Changes.AddRange(
            new ChangeLogEntry { Id = Guid.NewGuid(), At = cutoff.AddHours(-1), Action = ChangeAction.Criado, Target = "old", DetailKind = ChangeDetailKind.Program },
            new ChangeLogEntry { Id = Guid.NewGuid(), At = cutoff.AddHours(1), Action = ChangeAction.Criado, Target = "new", DetailKind = ChangeDetailKind.Program });
        await db.SaveChangesAsync();

        var service = new ReportService(db);
        var page = await service.ChangesAsync(new ReportQuery(Before: cutoff));

        Assert.Equal(1, page.Total); // the cursor (exclusive) drops the newer row
        Assert.Equal("old", page.Items[0].Target);
    }

    [Fact]
    public async Task ChangeAsync_consolidates_role_rows_into_a_diff_with_summary_and_curves()
    {
        await using var db = NewContext();
        var id = Guid.NewGuid();
        // idx1 changed (100→110), idx2 unchanged, idx3 removed, idx4 added.
        db.Changes.Add(new ChangeLogEntry
        {
            Id = id,
            At = new DateTimeOffset(2026, 5, 31, 12, 0, 0, TimeSpan.Zero),
            Action = ChangeAction.Editado,
            Target = "Perfil X",
            ProgramId = "prog-x",
            DetailKind = ChangeDetailKind.Program,
            Points =
            [
                new() { Index = 1, Temp = 100, TimeSec = 10, Ramp = RampShape.Linear, Role = ChangePointRole.ChangedBefore },
                new() { Index = 1, Temp = 110, TimeSec = 10, Ramp = RampShape.Linear, Role = ChangePointRole.ChangedAfter },
                new() { Index = 2, Temp = 150, TimeSec = 20, Ramp = RampShape.Linear, Role = ChangePointRole.Unchanged },
                new() { Index = 3, Temp = 180, TimeSec = 30, Ramp = RampShape.Linear, Role = ChangePointRole.Removed },
                new() { Index = 4, Temp = 60, TimeSec = 40, Ramp = RampShape.Linear, Role = ChangePointRole.Added },
            ],
        });
        await db.SaveChangesAsync();

        var detail = await new ReportService(db).ChangeAsync(id);

        Assert.NotNull(detail.Diff);
        var s = detail.Diff!.Summary;
        Assert.Equal(3, s.TotalChanges);  // changed + removed + added (unchanged excluded)
        Assert.Equal(1, s.Changed);
        Assert.Equal(1, s.Added);
        Assert.Equal(1, s.Removed);
        Assert.Equal(1, s.Unchanged);
        Assert.Equal(1, s.ChangedFields["temp"]);

        Assert.Equal(4, detail.Diff.Points.Count); // the changed pair collapses to one row
        Assert.Equal("changed", detail.Diff.Points[0].Status);
        Assert.Contains("temp", detail.Diff.Points[0].ChangedFields);

        // Curves are the REAL expanded setpoint profile, not bare vertices: the t=0 baseline is prepended,
        // so before (idx 1,2,3) and after (idx 1,2,4) become 4 points each, starting at (0,0).
        Assert.Equal(4, detail.BeforeCurve!.Count);
        Assert.Equal(0d, detail.BeforeCurve[0].T);
        Assert.Equal(0d, detail.BeforeCurve[0].Temp);
        Assert.Equal(4, detail.AfterCurve!.Count);
        Assert.Equal(0d, detail.AfterCurve[0].T);
    }

    [Fact]
    public async Task ChangeAsync_rebuilds_real_curve_from_segments()
    {
        // A creation snapshot exactly as ProgramService.BuildPoints emits it for a segment-built program:
        // per-leg vertices carrying cumulative time, the leg's raw temp and its ramp. The Fixo leg stores a
        // raw temp (0 here) that the real curve must ignore (it holds the prior 150 °C).
        await using var db = NewContext();
        var id = Guid.NewGuid();
        db.Changes.Add(new ChangeLogEntry
        {
            Id = id,
            At = new DateTimeOffset(2026, 6, 3, 12, 0, 0, TimeSpan.Zero),
            Action = ChangeAction.Criado,
            Target = "Perfil Seg",
            ProgramId = "prog-seg",
            DetailKind = ChangeDetailKind.Program,
            Points =
            [
                new() { Index = 1, Temp = 150, TimeSec = 90,  Ramp = RampShape.Linear,           Role = ChangePointRole.Added },
                new() { Index = 2, Temp = 0,   TimeSec = 150, Ramp = RampShape.Fixo,             Role = ChangePointRole.Added },
                new() { Index = 3, Temp = 220, TimeSec = 210, Ramp = RampShape.ParabolaPositiva, Role = ChangePointRole.Added },
            ],
        });
        await db.SaveChangesAsync();

        var detail = await new ReportService(db).ChangeAsync(id);
        var after = detail.AfterCurve!;

        // The rebuilt curve must equal the real expanded profile of the same legs.
        var expected = ProfileBuilder.ToProfile(
        [
            new ProfileSegment { Temp = 150, DurationSec = 90, Ramp = RampShape.Linear },
            new ProfileSegment { Temp = 0,   DurationSec = 60, Ramp = RampShape.Fixo },
            new ProfileSegment { Temp = 220, DurationSec = 60, Ramp = RampShape.ParabolaPositiva },
        ]);
        Assert.Equal(expected.Count, after.Count);
        for (var k = 0; k < expected.Count; k++)
        {
            Assert.Equal(expected[k].T, after[k].T, 3);
            Assert.Equal(expected[k].Temp, after[k].Temp, 3);
        }

        Assert.Equal(0d, after[0].T);   // baseline restored
        Assert.Equal(0d, after[0].Temp);
        var fixoPoint = after.First(p => Math.Abs(p.T - 150) < 0.5);
        Assert.Equal(150d, fixoPoint.Temp); // Fixo holds the prior temp, not its raw stored 0
        // Parabola is a real curve, not a chord: a positive parabola bows below the straight 150→220 line.
        var mid = after.First(p => p.T > 150 && p.T < 210);
        Assert.True(mid.Temp < 150 + (220 - 150) * ((mid.T - 150) / 60), "parabola should bow below the chord");
        Assert.Equal(220d, after[^1].Temp);
    }

    // ---- helpers ---------------------------------------------------------

    private static ReflowDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ReflowDbContext>()
            .UseInMemoryDatabase($"changes-filter-{Guid.NewGuid():N}")
            .Options;
        return new ReflowDbContext(options);
    }

    private static ChangeLogEntry ProgramChange(string programId, string target, int minute) => new()
    {
        Id = Guid.NewGuid(),
        At = new DateTimeOffset(2026, 5, 31, 12, minute, 0, TimeSpan.Zero),
        Action = ChangeAction.Editado,
        Target = target,
        ProgramId = programId,
        DetailKind = ChangeDetailKind.Program,
    };

    private static ChangeLogEntry ConfigChange(int minute) => new()
    {
        Id = Guid.NewGuid(),
        At = new DateTimeOffset(2026, 5, 31, 12, minute, 0, TimeSpan.Zero),
        Action = ChangeAction.Editado,
        Target = "Configuração do sistema",
        DetailKind = ChangeDetailKind.Config,
    };
}
