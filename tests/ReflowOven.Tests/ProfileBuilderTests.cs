using ReflowOven.Application.Common;
using ReflowOven.Domain.Entities;
using ReflowOven.Domain.Enums;
using ReflowOven.Domain.Hardware;
using Xunit;

namespace ReflowOven.Tests;

public class ProfileBuilderTests
{
    [Fact]
    public void Linear_segment_adds_a_single_endpoint()
    {
        var profile = ProfileBuilder.ToProfile([new ProfileSegment { Temp = 150, DurationSec = 90, Ramp = RampShape.Linear }]);

        Assert.Equal(2, profile.Count);
        Assert.Equal(0, profile[0].T);
        Assert.Equal(0, profile[0].Temp); // START_TEMP (baseline)
        Assert.Equal(90, profile[1].T);
        Assert.Equal(150, profile[1].Temp);
    }

    [Fact]
    public void Fixo_holds_the_incoming_temperature()
    {
        var profile = ProfileBuilder.ToProfile(
        [
            new ProfileSegment { Temp = 150, DurationSec = 90, Ramp = RampShape.Linear },
            new ProfileSegment { Temp = 999, DurationSec = 60, Ramp = RampShape.Fixo },
        ]);

        Assert.Equal(150, profile[^1].T + 0); // last point is at t = 90 + 60
        Assert.Equal(150, profile[^1].Temp);  // held at the previous temp, not 999
    }

    [Fact]
    public void Parabola_emits_twelve_subpoints()
    {
        var profile = ProfileBuilder.ToProfile([new ProfileSegment { Temp = 200, DurationSec = 120, Ramp = RampShape.ParabolaPositiva }]);

        Assert.Equal(13, profile.Count); // start + 12 interpolated
        Assert.Equal(200, profile[^1].Temp, 3);
    }

    [Fact]
    public void ToProfile_rounds_every_point_to_two_decimals()
    {
        // Measurement data is stored/streamed at 2 dp; a parabola leg otherwise yields temps like 3.4722…°C.
        var profile = ProfileBuilder.ToProfile([new ProfileSegment { Temp = 500, DurationSec = 3600, Ramp = RampShape.ParabolaPositiva }]);

        Assert.All(profile, p =>
        {
            Assert.Equal(p.T, Math.Round(p.T, 2));
            Assert.Equal(p.Temp, Math.Round(p.Temp, 2));
        });
        Assert.Equal((300d, 3.47d), (profile[1].T, profile[1].Temp)); // 500·(1/12)² = 3.4722… → 3.47
    }

    [Fact]
    public void TempAt_interpolates_and_clamps_to_the_ends()
    {
        List<ProfilePoint> p = [new() { T = 0, Temp = 0 }, new() { T = 10, Temp = 100 }];

        Assert.Equal(50, ProfileBuilder.TempAt(p, 5));
        Assert.Equal(0, ProfileBuilder.TempAt(p, -5));
        Assert.Equal(100, ProfileBuilder.TempAt(p, 999));
        Assert.Equal(10, ProfileBuilder.TotalTime(p));
    }

    [Fact]
    public void StageBoundaries_emits_one_leg_per_segment_collapsing_parabola_subpoints()
    {
        var stages = ProfileBuilder.StageBoundaries(
        [
            new ProfileSegment { Temp = 150, DurationSec = 90, Ramp = RampShape.Linear },
            new ProfileSegment { Temp = 999, DurationSec = 60, Ramp = RampShape.Fixo },
            new ProfileSegment { Temp = 220, DurationSec = 120, Ramp = RampShape.ParabolaPositiva },
        ]);

        Assert.Equal(3, stages.Count);                 // one boundary per leg, not per sub-point
        Assert.Equal((90, 150), (stages[0].T, stages[0].Temp));
        Assert.Equal((150, 150), (stages[1].T, stages[1].Temp)); // Fixo holds the prior temp
        Assert.Equal((270, 220), (stages[2].T, stages[2].Temp)); // parabola collapsed to its endpoint
    }

    [Fact]
    public void BuildComparison_matches_measured_temp_per_stage_with_zero_time_drift_on_a_clean_run()
    {
        // Two legs ending at t=10 (→100°C) and t=20 (→200°C); a full run captures samples through t=20.
        List<ProfilePoint> stages = [new() { T = 10, Temp = 100 }, new() { T = 20, Temp = 200 }];
        var samples = Trace(0, 20, t => 10 * t - 5); // measured grill = 95°C @t=10, 195°C @t=20

        var rows = ProfileBuilder.BuildComparison(stages, samples);

        Assert.Equal(2, rows.Count);
        Assert.Equal((1, 100, 95, 10, 10), Row(rows[0]));
        Assert.Equal((2, 200, 195, 10, 10), Row(rows[1])); // time tracks wall-clock → real == prog
    }

    [Fact]
    public void BuildComparison_truncates_the_interrupted_stage_and_drops_the_unreached_ones()
    {
        // Three legs (10/20/30 s); the run aborts at t=15, mid-second-stage.
        List<ProfilePoint> stages = [new() { T = 10, Temp = 100 }, new() { T = 20, Temp = 200 }, new() { T = 30, Temp = 300 }];
        var samples = Trace(0, 15, t => 10 * t - 5);

        var rows = ProfileBuilder.BuildComparison(stages, samples);

        Assert.Equal(2, rows.Count);                       // third stage never started → dropped
        Assert.Equal((1, 100, 95, 10, 10), Row(rows[0]));
        Assert.Equal((2, 200, 145, 10, 5), Row(rows[1]));  // truncated: 5 s of real time, measured 145°C
    }

    [Fact]
    public void BuildComparison_returns_empty_when_there_are_no_samples()
    {
        List<ProfilePoint> stages = [new() { T = 10, Temp = 100 }];

        Assert.Empty(ProfileBuilder.BuildComparison(stages, []));
    }

    private static List<TraceSample> Trace(int fromT, int toT, Func<int, double> oven)
    {
        var list = new List<TraceSample>();
        for (var t = fromT; t <= toT; t++)
            list.Add(new TraceSample(t, 0, oven(t), 0, 0, 0, 0, 0));
        return list;
    }

    private static (int, int, int, int, int) Row(ProfileComparisonRow r) =>
        (r.StageIndex, r.TempProg, r.TempReal, r.TimeProgSeconds, r.TimeRealSeconds);
}
