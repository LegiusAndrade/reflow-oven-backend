using Microsoft.AspNetCore.SignalR;

namespace ReflowOven.Api.Realtime;

/// <summary>
/// Live execution trace. Clients join a per-run group via <see cref="SubscribeRun"/>; the server
/// pushes TraceSample / RunPhaseChanged / RunStatusChanged / RunCompleted to that group.
/// </summary>
[Authorize]
public sealed class RunTelemetryHub : Hub
{
    public Task SubscribeRun(Guid runId) => Groups.AddToGroupAsync(Context.ConnectionId, GroupName(runId));

    public Task UnsubscribeRun(Guid runId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(runId));

    public static string GroupName(Guid runId) => $"run:{runId}";
}
