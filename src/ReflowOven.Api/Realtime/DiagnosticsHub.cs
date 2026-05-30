using Microsoft.AspNetCore.SignalR;

namespace ReflowOven.Api.Realtime;

/// <summary>Live sensor readings for the Diagnóstico screen. Server pushes ReadingTick to all clients at 1 Hz.</summary>
[Authorize]
public sealed class DiagnosticsHub : Hub;
