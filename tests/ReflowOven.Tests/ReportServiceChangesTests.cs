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
