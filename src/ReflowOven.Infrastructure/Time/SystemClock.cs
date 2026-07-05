using System.Diagnostics;

namespace ReflowOven.Infrastructure.Time;

/// <summary>
/// Production clock. <see cref="UtcNow"/> is the OS wall clock — on the device it is RTC-backed
/// (the ISL1208 restores it at boot; NTP disciplines it when online), the authoritative source for
/// stored timestamps (reports, audit, JWT iat/exp). Durations must NOT be derived from it: a step
/// correction mid-run would finish a burn early or hold a timeout open. <see cref="GetTimestamp"/> /
/// <see cref="GetElapsedTime"/> use the monotonic <see cref="Stopwatch"/> clock instead.
/// </summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public long GetTimestamp() => Stopwatch.GetTimestamp();

    public TimeSpan GetElapsedTime(long startTimestamp) => Stopwatch.GetElapsedTime(startTimestamp);
}
