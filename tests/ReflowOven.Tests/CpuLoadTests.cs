using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ReflowOven.Domain.Abstractions;
using ReflowOven.Infrastructure.Platform;
using Xunit;

namespace ReflowOven.Tests;

/// <summary>
/// Covers the CPU-load metric the Manutenção/Sistema screens show next to "Espaço livre no HD".
/// The Linux figure is a /proc/stat <c>busy/total</c> delta averaged over all cores: the pure
/// <see cref="LinuxSystemController.CpuBusyPercent"/> formula is asserted directly (the actual /proc
/// read isn't unit-testable), and the simulator is checked to stay in a plausible 0–100 range.
/// </summary>
public sealed class CpuLoadTests
{
    [Theory]
    // idleDelta, totalDelta, expected %
    [InlineData(0, 0, 0)]        // empty window (two reads too close / clock not advanced) ⇒ 0, no divide-by-zero
    [InlineData(100, 100, 0)]    // every jiffy idle ⇒ 0% busy
    [InlineData(0, 100, 100)]    // every jiffy busy ⇒ 100%
    [InlineData(75, 100, 25)]    // 25 of 100 jiffies busy ⇒ 25%
    [InlineData(875, 1000, 12.5)] // rounds to one decimal
    public void CpuBusyPercent_computes_busy_over_total(long idleDelta, long totalDelta, double expected) =>
        Assert.Equal(expected, LinuxSystemController.CpuBusyPercent(idleDelta, totalDelta));

    [Fact]
    public void CpuBusyPercent_clamps_a_negative_or_overshooting_delta_into_0_to_100()
    {
        // A counter wrap or idle moving faster than total (shouldn't happen, but be defensive) must not
        // produce a value outside 0–100.
        Assert.Equal(0, LinuxSystemController.CpuBusyPercent(idleDelta: 200, totalDelta: 100)); // idle > total ⇒ clamp low
        Assert.Equal(100, LinuxSystemController.CpuBusyPercent(idleDelta: -50, totalDelta: 100)); // negative idle ⇒ clamp high
    }

    [Fact]
    public async Task Simulated_controller_returns_a_plausible_cpu_load()
    {
        var sim = new SimulatedSystemController(new FixedClock(), Options.Create(new SystemOptions()),
            NullLogger<SimulatedSystemController>.Instance);

        var cpu = await sim.GetCpuLoadPercentAsync();
        Assert.InRange(cpu, 0, 100);

        // The full-metrics path must report the same kind of value next to the disk figures.
        var metrics = await sim.GetMetricsAsync();
        Assert.InRange(metrics.CpuLoadPercent, 0, 100);
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 5, 31, 12, 0, 0, TimeSpan.Zero);
    }
}
