using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ReflowOven.Application.Abstractions;
using ReflowOven.Application.Common;
using ReflowOven.Application.Dtos;
using ReflowOven.Application.Services;
using ReflowOven.Domain.Abstractions;
using ReflowOven.Domain.Entities;
using ReflowOven.Domain.Enums;
using ReflowOven.Infrastructure.Persistence;
using Xunit;

namespace ReflowOven.Tests;

/// <summary>
/// Covers the antes×depois change-log diff (TODO item H): editing a program must record BOTH the
/// previous curve (<see cref="ChangePointRole.ChangedBefore"/>) and the new curve
/// (<see cref="ChangePointRole.ChangedAfter"/>) in the single <see cref="ChangeLogEntry"/> the
/// <c>GET /api/changes/{id}</c> endpoint returns, so the frontend can draw both curves.
///
/// Like <see cref="SystemServiceAuditTests"/> this runs against a real <see cref="ReflowDbContext"/>
/// backed by the EF Core InMemory provider (fresh store per test). The owned-JSON <c>Points</c>
/// collection is kept in the in-memory object graph, so it round-trips without a jsonb provider.
/// </summary>
public sealed class ProgramServiceChangeLogTests
{
    [Fact]
    public async Task UpdateAsync_records_both_before_and_after_points()
    {
        await using var db = NewContext();

        // Seed an existing program built from two segments (the "before" curve).
        var oldSegments = new List<ProfileSegment>
        {
            new() { Temp = 150, DurationSec = 60, Ramp = RampShape.Linear },
            new() { Temp = 220, DurationSec = 30, Ramp = RampShape.Linear },
        };
        db.Programs.Add(new ReflowProgram
        {
            Id = "p1",
            Name = "Perfil antigo",
            IsSeed = false,
            Segments = oldSegments,
            Profile = ProfileBuilder.ToProfile(oldSegments),
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var service = NewService(db);

        // Edit it with a different three-segment curve (the "after" curve).
        var req = new SaveProgramRequest(
            Name: "Perfil novo",
            Description: null,
            Segments:
            [
                new ProfileSegmentDto(160, 90, RampShape.Linear),
                new ProfileSegmentDto(245, 45, RampShape.ParabolaPositiva),
                new ProfileSegmentDto(50, 120, RampShape.Linear),
            ],
            Profile: null);

        await service.UpdateAsync("p1", req, userId: null);

        var change = await db.Changes.SingleAsync();
        Assert.Equal(ChangeAction.Editado, change.Action);

        var before = change.Points.Where(p => p.Role == ChangePointRole.ChangedBefore).OrderBy(p => p.Index).ToList();
        var after = change.Points.Where(p => p.Role == ChangePointRole.ChangedAfter).OrderBy(p => p.Index).ToList();

        // Before = the two old segments; After = the three new ones — both fully present.
        Assert.Equal(2, before.Count);
        Assert.Equal(3, after.Count);

        // Cumulative times and temps match each source segment list (TimeSec is the running total).
        Assert.Equal([150, 220], before.Select(p => p.Temp));
        Assert.Equal([60, 90], before.Select(p => p.TimeSec));

        Assert.Equal([160, 245, 50], after.Select(p => p.Temp));
        Assert.Equal([90, 135, 255], after.Select(p => p.TimeSec));
        Assert.Equal(RampShape.ParabolaPositiva, after[1].Ramp);

        // 1-based indices restart per role, so each curve is independently ordered.
        Assert.Equal([1, 2], before.Select(p => p.Index));
        Assert.Equal([1, 2, 3], after.Select(p => p.Index));
    }

    // ---- helpers ---------------------------------------------------------

    private static ReflowDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ReflowDbContext>()
            .UseInMemoryDatabase($"changelog-{Guid.NewGuid():N}")
            .Options;
        return new ReflowDbContext(options);
    }

    private static ProgramService NewService(ReflowDbContext db)
    {
        var clock = new FixedClock();
        var current = new AnonymousUser();
        var audit = new AuditService(db, clock, current, NullLogger<AuditService>.Instance);
        return new ProgramService(db, clock, audit);
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 5, 31, 12, 0, 0, TimeSpan.Zero);
    }

    // A user-less (technician-like) principal: no per-user activity counter is bumped, which keeps
    // the test focused on the change-log diff.
    private sealed class AnonymousUser : ICurrentUser
    {
        public bool IsAuthenticated => false;
        public string? Id => null;
        public Guid? UserId => null;
        public string? Name => null;
        public UserType? Role => null;
        public bool IsCalibration => false;
    }
}
