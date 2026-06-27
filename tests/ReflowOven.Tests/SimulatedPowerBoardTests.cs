using ReflowOven.Application.Common;
using ReflowOven.Application.Dtos;
using ReflowOven.Domain.Abstractions;
using ReflowOven.Domain.Entities;
using ReflowOven.Domain.Enums;
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

        await board.StartProgramAsync([], profile); // sim ignores segments and interpolates the sampled profile
        clock.UtcNow = DateTimeOffset.UnixEpoch.AddSeconds(50); // setpoint ≈ 125 °C

        var r = await board.ReadAsync();
        Assert.InRange(r.OvenTempC, 110, 140); // ≈125 ± jitter
    }

    [Fact]
    public async Task Acknowledge_fault_completes_and_leaves_readings_fault_free()
    {
        var board = new SimulatedPowerBoard(new FixedClock(DateTimeOffset.UnixEpoch));
        await board.AcknowledgeFaultAsync(); // simulator stays on the happy path → clears to OK
        var r = await board.ReadAsync();
        Assert.Null(r.FaultCode);
    }
}

/// <summary>The diagnostics <c>fault</c> field is a frontend contract: a latched E-code maps through the
/// seeded catalog to <c>{ code, severity, message }</c>, and an OK board maps to null.</summary>
public class SensorReadingsFaultMappingTests
{
    private static SensorReadings Reading(string? faultCode) =>
        new(BoardTempC: 30, BoardFanRpm: 1000, OvenTempC: 120, OvenFanRpm: 1200, VoltageV: 24, CurrentA: 2, FaultCode: faultCode);

    [Fact]
    public void Latched_fault_maps_through_the_seeded_catalog()
    {
        var dto = SensorReadingsDto.From(Reading("E-101"));
        Assert.NotNull(dto.Fault);
        Assert.Equal("E-101", dto.Fault!.Code);
        Assert.Equal(ErrorSeverity.Critico, dto.Fault.Severity); // E-101 is Crítico in the seeded catalog
        Assert.False(string.IsNullOrWhiteSpace(dto.Fault.Message));
    }

    [Fact]
    public void Ok_board_maps_to_null_fault()
    {
        var dto = SensorReadingsDto.From(Reading(null));
        Assert.Null(dto.Fault);
    }

    [Fact]
    public void Unknown_code_falls_back_to_critical()
    {
        var (severity, message) = FaultCatalog.Resolve("E-999");
        Assert.Equal(ErrorSeverity.Critico, severity);
        Assert.False(string.IsNullOrWhiteSpace(message));
    }
}
