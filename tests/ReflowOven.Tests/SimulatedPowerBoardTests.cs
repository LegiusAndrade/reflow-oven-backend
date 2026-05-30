using ReflowOven.Domain.Abstractions;
using ReflowOven.Domain.Entities;
using ReflowOven.Domain.Hardware;
using ReflowOven.Infrastructure.Hardware;
using Xunit;

namespace ReflowOven.Tests;

public class SimulatedPowerBoardTests
{
    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = now;
    }

    [Fact]
    public async Task Idle_readings_stay_within_clamps()
    {
        var board = new SimulatedPowerBoard(new FixedClock(DateTimeOffset.UnixEpoch));
        var r = await board.ReadAsync();

        Assert.InRange(r.OvenTempC, 0, 300);
        Assert.InRange(r.BoardTempC, 0, 200);
        Assert.InRange(r.CurrentA, 0, 60);
        Assert.InRange(r.VoltageV, 0, 250);
    }

    [Fact]
    public async Task Oven_temperature_tracks_the_running_profile()
    {
        var clock = new FixedClock(DateTimeOffset.UnixEpoch);
        var board = new SimulatedPowerBoard(clock);
        List<ProfilePoint> profile = [new() { T = 0, Temp = 25 }, new() { T = 100, Temp = 225 }];

        await board.StartProgramAsync(profile, new ProcessLimits(300, 5000, 100, 250, 60));
        clock.UtcNow = DateTimeOffset.UnixEpoch.AddSeconds(50); // setpoint ≈ 125 °C

        var r = await board.ReadAsync();
        Assert.InRange(r.OvenTempC, 110, 140); // ≈125 ± jitter
    }
}
