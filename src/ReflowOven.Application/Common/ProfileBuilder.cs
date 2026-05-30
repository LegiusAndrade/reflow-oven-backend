namespace ReflowOven.Application.Common;

/// <summary>
/// Server-side port of the frontend profile math (ProgramEditorScreen.toProfile + run.ts).
/// Derives the sampled setpoint curve from editable segments and interpolates it during a run.
/// </summary>
public static class ProfileBuilder
{
    /// <summary>Expand editable segments into the sampled curve (Linear / Fixo / parabola, 12 sub-points).</summary>
    public static List<ProfilePoint> ToProfile(IReadOnlyList<ProfileSegment> segments)
    {
        var points = new List<ProfilePoint> { new() { T = 0, Temp = DomainConstants.StartTemp } };
        double t = 0;
        double prevTemp = DomainConstants.StartTemp;

        foreach (var s in segments)
        {
            var d = Math.Max(0, s.DurationSec);

            if (s.Ramp == RampShape.Fixo)
            {
                t += d;
                points.Add(new() { T = t, Temp = prevTemp }); // hold at the current temperature
                continue;
            }

            double target = s.Temp;
            if (s.Ramp == RampShape.Linear)
            {
                t += d;
                points.Add(new() { T = t, Temp = target });
            }
            else
            {
                // positiva: slow start, fast finish (concave up); negativa: the opposite.
                Func<double, double> ease = s.Ramp == RampShape.ParabolaPositiva
                    ? x => x * x
                    : x => 2 * x - x * x;
                var t0 = t;
                const int steps = 12;
                for (var k = 1; k <= steps; k++)
                {
                    var x = (double)k / steps;
                    points.Add(new() { T = t0 + x * d, Temp = prevTemp + (target - prevTemp) * ease(x) });
                }
                t = t0 + d;
            }

            prevTemp = target;
        }

        return points;
    }

    /// <summary>Total run length in seconds (the last setpoint's time).</summary>
    public static double TotalTime(IReadOnlyList<ProfilePoint> profile)
        => profile.Count > 0 ? profile[^1].T : 0;

    /// <summary>Linear-interpolate the setpoint temperature at time <paramref name="t"/> (clamped to ends).</summary>
    public static double TempAt(IReadOnlyList<ProfilePoint> profile, double t)
    {
        if (profile.Count == 0) return 0;
        if (t <= profile[0].T) return profile[0].Temp;
        var last = profile[^1];
        if (t >= last.T) return last.Temp;

        for (var i = 1; i < profile.Count; i++)
        {
            var b = profile[i];
            if (t <= b.T)
            {
                var a = profile[i - 1];
                var span = b.T - a.T;
                if (span == 0) span = 1;
                return a.Temp + (b.Temp - a.Temp) * (t - a.T) / span;
            }
        }
        return last.Temp;
    }

    /// <summary>Classify the moment in the run from the local slope and proximity to the peak.</summary>
    public static RunPhase PhaseAt(IReadOnlyList<ProfilePoint> profile, double t)
    {
        if (profile.Count == 0) return RunPhase.Patamar;

        var peak = profile[0];
        foreach (var p in profile)
            if (p.Temp > peak.Temp) peak = p;

        if (Math.Abs(t - peak.T) <= 8) return RunPhase.Pico;

        var slope = TempAt(profile, t + 2) - TempAt(profile, t);
        if (slope > 0.5) return RunPhase.Aquecimento;
        if (slope < -0.5) return RunPhase.Resfriamento;
        return RunPhase.Patamar;
    }
}
