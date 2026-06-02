namespace ReflowOven.Application.Dtos;

/// <summary>
/// One notification-feed entry (the TopBar bell + the Notificações screen), mapping <see cref="Notification"/>.
/// <para><c>At</c> is ISO 8601 (the backend timestamp contract); the frontend converts it to the epoch-ms
/// it keeps in its local <c>AppNotification.at</c>. <c>Kind</c> serializes to the lowercase wire literals
/// <c>info</c>/<c>error</c>/<c>update</c> the bell expects.</para>
/// </summary>
public sealed record NotificationDto(
    string Id,
    NotificationFeedKind Kind,
    DateTimeOffset At,
    string Title,
    string Message,
    bool Read)
{
    public static NotificationDto From(Notification n) =>
        new(n.Id.ToString(), n.Kind, n.At, n.Title, n.Message, n.Read);
}

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
