namespace ReflowOven.Application.Services;

/// <summary>No-op fallback so the DI graph resolves without the Api SignalR layer (seed-only host, tests).
/// The Api registers <c>SignalRNotificationSink</c> which overrides this.</summary>
public sealed class NullNotificationSink : INotificationSink
{
    public Task PublishAsync(NotificationDto entry) => Task.CompletedTask;
}
