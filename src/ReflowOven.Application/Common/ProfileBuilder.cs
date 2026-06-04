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

        // Measurement data — the stored profile and the curve the API returns — is kept at 2 decimals;
        // a parabola leg otherwise yields temps like 3.4722…°C. Configuration values (PID gains,
        // calibration offset/gain) are never rounded here — they keep full precision.
        return [.. points.Select(p => new ProfilePoint { T = Math.Round(p.T, 2), Temp = Math.Round(p.Temp, 2) })];
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

    /// <summary>
    /// The logical stage boundaries — one entry per editable segment, carrying that leg's cumulative
    /// end-time and target temperature. Collapses a parabola's 12 sub-points into the single endpoint
    /// <see cref="ToProfile"/> lands on, so the per-stage comparison table gets one row per leg rather
    /// than one per sampled point. Mirrors ToProfile's time/target math (Fixo holds the prior temp).
    /// </summary>
    public static List<ProfilePoint> StageBoundaries(IReadOnlyList<ProfileSegment> segments)
    {
        var stages = new List<ProfilePoint>(segments.Count);
        double t = 0;
        double prevTemp = DomainConstants.StartTemp;
        foreach (var s in segments)
        {
            t += Math.Max(0, s.DurationSec);
            var target = s.Ramp == RampShape.Fixo ? prevTemp : s.Temp; // Fixo holds the incoming temperature
            stages.Add(new ProfilePoint { T = t, Temp = target });
            prevTemp = target;
        }
        return stages;
    }

    /// <summary>
    /// Build the report's "Comparativo do Perfil" rows: for each logical stage, the programmed target
    /// vs. the measured grill temperature at the stage's end, and the programmed vs. actual elapsed
    /// duration. A run that ended early (abort/fault) truncates the stage it stopped in and drops the
    /// stages it never reached, so each row reflects telemetry that actually happened. The client
    /// derives the "Desvio" column from these (tempReal − tempProg, timeReal − timeProg).
    /// </summary>
    public static List<ProfileComparisonRow> BuildComparison(
        IReadOnlyList<ProfilePoint> stages, IReadOnlyList<TraceSample> samples)
    {
        if (stages.Count == 0 || samples.Count == 0) return [];
        var runEnd = samples[^1].T; // true elapsed at the last captured tick

        var rows = new List<ProfileComparisonRow>(stages.Count);
        double prevT = 0;
        foreach (var stage in stages)
        {
            if (prevT >= runEnd) break; // this stage never started — the run stopped before it
            var realEnd = Math.Min(stage.T, runEnd);
            rows.Add(new ProfileComparisonRow
            {
                StageIndex = rows.Count + 1,
                TempProg = (int)Math.Round(stage.Temp),
                TempReal = (int)Math.Round(MeasuredTempAt(samples, realEnd)),
                TimeProgSeconds = (int)Math.Round(stage.T - prevT),
                TimeRealSeconds = Math.Max(0, (int)Math.Round(realEnd - prevT)),
            });
            prevT = stage.T;
        }
        return rows;
    }

    /// <summary>Linear-interpolate the measured grill temperature from the captured samples at elapsed
    /// time <paramref name="t"/> (clamped to the ends). Samples are in tick order.</summary>
    private static double MeasuredTempAt(IReadOnlyList<TraceSample> samples, double t)
    {
        if (t <= samples[0].T) return samples[0].Oven;
        if (t >= samples[^1].T) return samples[^1].Oven;
        for (var i = 1; i < samples.Count; i++)
        {
            var b = samples[i];
            if (t <= b.T)
            {
                var a = samples[i - 1];
                var span = b.T - a.T;
                return span <= 0 ? b.Oven : a.Oven + (b.Oven - a.Oven) * (t - a.T) / span;
            }
        }
        return samples[^1].Oven;
    }
}
