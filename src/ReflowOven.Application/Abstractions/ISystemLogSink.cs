namespace ReflowOven.Application.Abstractions;

/// <summary>
/// Fans a freshly-written system-log line out to the SystemLog SignalR hub (implemented in the Api
/// layer, mirroring <see cref="ReflowOven.Domain.Abstractions.ITelemetrySink"/>). Decoupled so the
/// Application/Infrastructure writers don't reference SignalR. A no-op stub keeps non-Api hosts simple.
/// </summary>
public interface ISystemLogSink
{
    Task PublishAsync(SystemLogDto entry);
}
