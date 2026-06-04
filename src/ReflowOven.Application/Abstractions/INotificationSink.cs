namespace ReflowOven.Application.Abstractions;

/// <summary>
/// Fans a freshly-raised feed notification out to the Notifications SignalR hub (implemented in the Api
/// layer, mirroring <see cref="ISystemLogSink"/>). Decoupled so the Application/Infrastructure writers don't
/// reference SignalR. A no-op stub keeps non-Api hosts (seed-only, tests) simple.
/// </summary>
public interface INotificationSink
{
    Task PublishAsync(NotificationDto entry);
}
