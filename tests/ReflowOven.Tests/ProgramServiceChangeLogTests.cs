using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ReflowOven.Application.Abstractions;
using ReflowOven.Application.Common;
using ReflowOven.Application.Dtos;
using ReflowOven.Application.Services;
using ReflowOven.Domain.Abstractions;
using ReflowOven.Domain.Common;
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
    public async Task UpdateAsync_records_a_per_point_diff_with_changed_and_added_roles()
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

        // New curve: both existing points change value, and a third is appended.
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

        var before = ByRole(change, ChangePointRole.ChangedBefore);
        var after = ByRole(change, ChangePointRole.ChangedAfter);
        var added = ByRole(change, ChangePointRole.Added);

        // The two existing points changed: each appears as changed-before + changed-after at the same index.
        Assert.Equal([1, 2], before.Select(p => p.Index));
        Assert.Equal([150, 220], before.Select(p => p.Temp));
        Assert.Equal([60, 90], before.Select(p => p.TimeSec));

        Assert.Equal([1, 2], after.Select(p => p.Index));
        Assert.Equal([160, 245], after.Select(p => p.Temp));
        Assert.Equal([90, 135], after.Select(p => p.TimeSec));
        Assert.Equal(RampShape.ParabolaPositiva, after[1].Ramp);

        // The third point is appended, so it is `added` (only present in the new curve).
        Assert.Single(added);
        Assert.Equal(3, added[0].Index);
        Assert.Equal(50, added[0].Temp);
        Assert.Equal(255, added[0].TimeSec);

        Assert.Empty(ByRole(change, ChangePointRole.Removed));
        Assert.Empty(ByRole(change, ChangePointRole.Unchanged));

        // Reconstructed curves: before = removed+changed-before+unchanged (2 pts), after = the rest (3 pts).
        Assert.Equal(2, before.Count);
        Assert.Equal(3, after.Count + added.Count);
    }

    [Fact]
    public async Task UpdateAsync_marks_unchanged_changed_and_removed_points()
    {
        await using var db = NewContext();

        // Old curve: 3 segments.
        var oldSegments = new List<ProfileSegment>
        {
            new() { Temp = 100, DurationSec = 60, Ramp = RampShape.Linear },
            new() { Temp = 200, DurationSec = 60, Ramp = RampShape.Linear },
            new() { Temp = 50, DurationSec = 60, Ramp = RampShape.Linear },
        };
        db.Programs.Add(new ReflowProgram
        {
            Id = "p1",
            Name = "Antigo",
            IsSeed = false,
            Segments = oldSegments,
            Profile = ProfileBuilder.ToProfile(oldSegments),
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var service = NewService(db);

        // Point 1 identical, point 2 changed (200→210), point 3 dropped.
        var req = new SaveProgramRequest(
            Name: "Novo",
            Description: null,
            Segments:
            [
                new ProfileSegmentDto(100, 60, RampShape.Linear),
                new ProfileSegmentDto(210, 60, RampShape.Linear),
            ],
            Profile: null);

        await service.UpdateAsync("p1", req, userId: null);

        var change = await db.Changes.SingleAsync();

        var unchanged = ByRole(change, ChangePointRole.Unchanged);
        var before = ByRole(change, ChangePointRole.ChangedBefore);
        var after = ByRole(change, ChangePointRole.ChangedAfter);
        var removed = ByRole(change, ChangePointRole.Removed);

        Assert.Single(unchanged);
        Assert.Equal(1, unchanged[0].Index);
        Assert.Equal(100, unchanged[0].Temp);

        Assert.Single(before);
        Assert.Equal(2, before[0].Index);
        Assert.Equal(200, before[0].Temp);

        Assert.Single(after);
        Assert.Equal(2, after[0].Index);
        Assert.Equal(210, after[0].Temp);

        Assert.Single(removed);
        Assert.Equal(3, removed[0].Index);
        Assert.Equal(50, removed[0].Temp);

        Assert.Empty(ByRole(change, ChangePointRole.Added));
    }

    [Fact]
    public async Task CreateAsync_rejects_an_imported_profile_over_the_cap()
    {
        await using var db = NewContext();
        var service = NewService(db);

        var tooMany = Enumerable.Range(0, DomainConstants.ProfileMaxPoints + 1)
            .Select(k => new ProfilePointDto(k, 100)).ToList();
        var req = new SaveProgramRequest("Curva importada", null, Segments: null, Profile: tooMany);

        await Assert.ThrowsAsync<ValidationAppException>(() => service.CreateAsync(req));
    }

    [Fact]
    public async Task CreateAsync_accepts_an_imported_profile_at_the_cap()
    {
        await using var db = NewContext();
        var service = NewService(db);

        var atCap = Enumerable.Range(0, DomainConstants.ProfileMaxPoints)
            .Select(k => new ProfilePointDto(k, 100)).ToList();
        var req = new SaveProgramRequest("Curva importada", null, Segments: null, Profile: atCap);

        var dto = await service.CreateAsync(req);
        Assert.Equal(DomainConstants.ProfileMaxPoints, dto.Profile.Count);
    }

    [Fact]
    public async Task CreateAsync_rejects_an_imported_profile_point_out_of_temp_range()
    {
        await using var db = NewContext();
        var service = NewService(db);

        var req = new SaveProgramRequest("Curva importada", null, Segments: null,
            Profile: [new ProfilePointDto(0, 25), new ProfilePointDto(10, DomainConstants.PointTempMax + 1)]);

        await Assert.ThrowsAsync<ValidationAppException>(() => service.CreateAsync(req));
    }

    [Fact]
    public async Task Editing_the_same_program_keeps_only_the_retention_cap_of_most_recent_changes()
    {
        await using var db = NewContext();
        var service = NewService(db, new AdvancingClock());

        ProfileSegmentDto[] Seg(int temp) => [new ProfileSegmentDto(temp, 60, RampShape.Linear)];

        var created = await service.CreateAsync(new SaveProgramRequest("P0", null, Seg(150), null));

        // 12 edits on top of the Criado row => 13 change rows attempted; retention keeps the last 10.
        for (var k = 1; k <= 12; k++)
            await service.UpdateAsync(created.Id, new SaveProgramRequest($"E{k}", null, Seg(150 + k), null), userId: null);

        var rows = await db.Changes.Where(c => c.ProgramId == created.Id).ToListAsync();
        var targets = rows.Select(r => r.Target).ToList();

        Assert.Equal(DomainConstants.ChangeRetentionPerProgramMax, rows.Count); // exactly 10
        // The 3 oldest (Criado P0, E1, E2) were pruned; the 10 newest survive.
        Assert.Contains("E12", targets);
        Assert.Contains("E3", targets);
        Assert.DoesNotContain("P0", targets);
        Assert.DoesNotContain("E1", targets);
        Assert.DoesNotContain("E2", targets);
    }

    // ---- helpers ---------------------------------------------------------

    private static List<ChangePointRow> ByRole(ChangeLogEntry c, ChangePointRole role) =>
        c.Points.Where(p => p.Role == role).OrderBy(p => p.Index).ToList();

    private static ReflowDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ReflowDbContext>()
            .UseInMemoryDatabase($"changelog-{Guid.NewGuid():N}")
            .Options;
        return new ReflowDbContext(options);
    }

    private static ProgramService NewService(ReflowDbContext db, IClock? clock = null)
    {
        clock ??= new FixedClock();
        var current = new AnonymousUser();
        var audit = new AuditService(db, clock, current, NullLogger<AuditService>.Instance);
        return new ProgramService(db, clock, audit);
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 5, 31, 12, 0, 0, TimeSpan.Zero);
    }

    // Strictly-increasing clock so each recorded change gets a distinct, ordered timestamp — makes the
    // retention "keep the most recent N" pruning deterministic to assert.
    private sealed class AdvancingClock : IClock
    {
        private DateTimeOffset _now = new(2026, 5, 31, 12, 0, 0, TimeSpan.Zero);
        public DateTimeOffset UtcNow
        {
            get { var t = _now; _now = _now.AddSeconds(1); return t; }
        }
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
