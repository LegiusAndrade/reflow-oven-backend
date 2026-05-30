namespace ReflowOven.Domain.Abstractions;

/// <summary>Wall-clock abstraction so time-dependent logic stays testable.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
