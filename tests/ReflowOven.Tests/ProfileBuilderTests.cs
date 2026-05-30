using ReflowOven.Application.Common;
using ReflowOven.Domain.Entities;
using ReflowOven.Domain.Enums;
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
        Assert.Equal(25, profile[0].Temp); // START_TEMP
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
    public void TempAt_interpolates_and_clamps_to_the_ends()
    {
        List<ProfilePoint> p = [new() { T = 0, Temp = 0 }, new() { T = 10, Temp = 100 }];

        Assert.Equal(50, ProfileBuilder.TempAt(p, 5));
        Assert.Equal(0, ProfileBuilder.TempAt(p, -5));
        Assert.Equal(100, ProfileBuilder.TempAt(p, 999));
        Assert.Equal(10, ProfileBuilder.TotalTime(p));
    }
}
