using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ReflowOven.Application.Abstractions;
using ReflowOven.Application.Common;
using ReflowOven.Application.Dtos;
using ReflowOven.Application.Services;
using ReflowOven.Domain.Abstractions;
using ReflowOven.Domain.Common;
using ReflowOven.Domain.Enums;
using ReflowOven.Infrastructure.Persistence;
using Xunit;

namespace ReflowOven.Tests;

/// <summary>
/// The per-point temperature floor (<see cref="DomainConstants.PointTempMin"/> = 50 °C): a user may not
/// enter a profile point below it, EXCEPT the fixed t=0 baseline start (<see cref="DomainConstants.StartTemp"/>
/// = 0 °C), which the editor prepends and the import path accepts. Runs against the EF Core InMemory
/// provider, like <see cref="ProgramServiceChangeLogTests"/>.
/// </summary>
public sealed class ProgramServiceTempFloorTests
{
    [Fact]
    public async Task CreateAsync_rejects_a_segment_below_the_temp_floor()
    {
        var service = NewService(NewContext());
        var req = new SaveProgramRequest("Abaixo do piso", null,
            Segments: [new ProfileSegmentDto(DomainConstants.PointTempMin - 1, 60, RampShape.Linear)],
            Profile: null);

        await Assert.ThrowsAsync<ValidationAppException>(() => service.CreateAsync(req));
    }

    [Fact]
    public async Task CreateAsync_accepts_a_segment_at_the_temp_floor_and_keeps_the_baseline_start()
    {
        var service = NewService(NewContext());
        var req = new SaveProgramRequest("No piso", null,
            Segments: [new ProfileSegmentDto(DomainConstants.PointTempMin, 60, RampShape.Linear)],
            Profile: null);

        var dto = await service.CreateAsync(req);

        // The derived curve keeps the prepended baseline start (0 °C, below the floor by design)…
        Assert.Equal(0, dto.Profile[0].T);
        Assert.Equal(DomainConstants.StartTemp, dto.Profile[0].Temp);
        // …and reaches exactly the floor at the segment's end.
        Assert.Equal(DomainConstants.PointTempMin, dto.Profile[^1].Temp);
    }

    [Fact]
    public async Task CreateAsync_accepts_an_imported_profile_that_starts_at_baseline()
    {
        var service = NewService(NewContext());
        // The t=0 baseline start is below the floor but exempt; every later point is at/above it.
        var req = new SaveProgramRequest("Curva importada", null, Segments: null,
            Profile: [new ProfilePointDto(0, DomainConstants.StartTemp), new ProfilePointDto(10, 200), new ProfilePointDto(20, DomainConstants.PointTempMin)]);

        var dto = await service.CreateAsync(req);

        Assert.Equal(DomainConstants.StartTemp, dto.Profile[0].Temp);
        Assert.Equal(3, dto.Profile.Count);
    }

    [Fact]
    public async Task CreateAsync_rejects_an_imported_cooldown_point_below_the_floor()
    {
        var service = NewService(NewContext());
        // A t>0 cooldown point under the floor is rejected (only the t=0 start is exempt).
        var req = new SaveProgramRequest("Resfria demais", null, Segments: null,
            Profile: [new ProfilePointDto(0, DomainConstants.StartTemp), new ProfilePointDto(10, 200), new ProfilePointDto(20, DomainConstants.PointTempMin - 1)]);

        await Assert.ThrowsAsync<ValidationAppException>(() => service.CreateAsync(req));
    }

    // ---- helpers (mirror ProgramServiceChangeLogTests) -------------------------------------

    private static ReflowDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ReflowDbContext>()
            .UseInMemoryDatabase($"tempfloor-{Guid.NewGuid():N}")
            .Options;
        return new ReflowDbContext(options);
    }

    private static ProgramService NewService(ReflowDbContext db)
    {
        var clock = new FixedClock();
        var current = new AnonymousUser();
        var audit = new AuditService(db, clock, current, NullLogger<AuditService>.Instance);
        return new ProgramService(db, clock, audit, current);
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
    }

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
