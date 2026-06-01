using Microsoft.AspNetCore.SignalR;

namespace ReflowOven.Api.Realtime;

/// <summary>Bridges system-log writes to SignalR — broadcasts each new line to every client (the feed is
/// device-wide, like the diagnostics tick). Singleton; depends only on the hub context.</summary>
public sealed class SignalRSystemLogSink(IHubContext<SystemLogHub> hub) : ISystemLogSink
{
    public Task PublishAsync(SystemLogDto entry) =>
        hub.Clients.All.SendAsync("SystemLogLine", entry);
}
