namespace ReflowOven.Application.Dtos;

/// <summary>
/// One notification-feed entry (the TopBar bell + the Notificações screen), mapping <see cref="Notification"/>.
/// <para><c>At</c> is ISO 8601 (the backend timestamp contract); the frontend converts it to the epoch-ms
/// it keeps in its local <c>AppNotification.at</c>. <c>Kind</c> serializes to the lowercase wire literals
/// <c>info</c>/<c>warning</c>/<c>error</c>/<c>update</c> the bell expects. <c>DeepLink</c> (wire
/// <c>deepLink</c>, omitted when null) optionally points the UI at a screen — <c>{ tab, until }</c>.</para>
/// </summary>
public sealed record NotificationDto(
    string Id,
    NotificationFeedKind Kind,
    DateTimeOffset At,
    string Title,
    string Message,
    bool Read,
    NotificationDeepLinkDto? DeepLink = null)
{
    public static NotificationDto From(Notification n) =>
        new(n.Id.ToString(), n.Kind, n.At, n.Title, n.Message, n.Read,
            n.DeepLink is null ? null : new NotificationDeepLinkDto(n.DeepLink.Tab, n.DeepLink.Until));
}

/// <summary>Optional deep link of a feed entry — the frontend tab to open and the ISO-8601 cutoff the view
/// should filter to (<c>{ tab: "relatorios", until: "…" }</c>).</summary>
public sealed record NotificationDeepLinkDto(string Tab, DateTimeOffset Until);

/// <summary>Unread badge count for the bell.</summary>
public sealed record UnreadCountDto(int Count);

/// <summary>A soft-deleted feed entry as the Master's "Apagados" (trash) view consumes it.</summary>
public sealed record DeletedNotificationDto(
    string Id,
    NotificationFeedKind Kind,
    DateTimeOffset At,
    string Title,
    string Message,
    DateTimeOffset? DeletedAt,
    string? DeletedBy);
