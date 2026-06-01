using Microsoft.AspNetCore.SignalR;

namespace ReflowOven.Api.Realtime;

/// <summary>Live system-log lines (Diagnóstico → Log, "Sistema" source). The server pushes
/// <c>SystemLogLine</c> (a <see cref="SystemLogDto"/>) to all connected clients as each entry is written.</summary>
[Authorize]
public sealed class SystemLogHub : Hub;
