using Microsoft.EntityFrameworkCore;
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

        // BeforeCurve = removed + changed-before + unchanged (idx 1,2,3); AfterCurve = added + changed-after + unchanged (idx 1,2,4).
        Assert.Equal(3, detail.BeforeCurve!.Count);
        Assert.Equal(3, detail.AfterCurve!.Count);
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
