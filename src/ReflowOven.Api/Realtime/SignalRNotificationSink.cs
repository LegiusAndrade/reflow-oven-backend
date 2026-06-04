using Microsoft.AspNetCore.SignalR;

namespace ReflowOven.Api.Realtime;

/// <summary>Bridges notification writes to SignalR — broadcasts each new feed entry to every connected client
/// (the bell feed is device-wide, like the diagnostics tick). Singleton; depends only on the hub context.</summary>
public sealed class SignalRNotificationSink(IHubContext<NotificationHub> hub) : INotificationSink
{
    public Task PublishAsync(NotificationDto entry) =>
        hub.Clients.All.SendAsync("Notification", entry);
}
