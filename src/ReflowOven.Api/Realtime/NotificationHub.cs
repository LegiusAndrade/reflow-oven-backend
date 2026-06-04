using Microsoft.AspNetCore.SignalR;

namespace ReflowOven.Api.Realtime;

/// <summary>Live notification-feed entries (the TopBar bell). The server pushes <c>Notification</c>
/// (a <see cref="NotificationDto"/>) to all connected clients as each one is raised — so the bell updates
/// without the client polling <c>GET /api/notifications</c>.</summary>
[Authorize]
public sealed class NotificationHub : Hub;
