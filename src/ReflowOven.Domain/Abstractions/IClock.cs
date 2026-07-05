namespace ReflowOven.Domain.Abstractions;

/// <summary>Wall-clock abstraction so time-dependent logic stays testable.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }

    /// <summary>
    /// Opaque monotonic timestamp for measuring elapsed time (pair with <see cref="GetElapsedTime"/>).
    /// Unlike <see cref="UtcNow"/> it is immune to wall-clock steps — an NTP correction, the boot-time
    /// restore from the hardware RTC, a manual time set — so run/tune durations and timeouts never
    /// stretch or jump. The default maps to <see cref="UtcNow"/> ticks so simple test fakes keep
    /// driving time through their fake wall clock; the production clock overrides with Stopwatch.
    /// </summary>
    long GetTimestamp() => UtcNow.UtcTicks;

    /// <summary>Elapsed time since a timestamp previously returned by <see cref="GetTimestamp"/>.</summary>
    TimeSpan GetElapsedTime(long startTimestamp) => TimeSpan.FromTicks(UtcNow.UtcTicks - startTimestamp);
}
