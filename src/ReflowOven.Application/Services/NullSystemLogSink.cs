namespace ReflowOven.Application.Services;

/// <summary>No-op fallback so the DI graph resolves without the Api SignalR layer (seed-only host, tests).
/// The Api registers <c>SignalRSystemLogSink</c> which overrides this.</summary>
public sealed class NullSystemLogSink : ISystemLogSink
{
    public Task PublishAsync(SystemLogDto entry) => Task.CompletedTask;
}
