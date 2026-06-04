using System.Text.Json.Serialization;

namespace ReflowOven.Domain.Entities;

/// <summary>Kind of a feed notification (matches the frontend bell: info / warning / error / update).</summary>
public enum NotificationFeedKind
{
    [JsonStringEnumMemberName("info")] Info,
    [JsonStringEnumMemberName("warning")] Warning,
    [JsonStringEnumMemberName("error")] Error,
    [JsonStringEnumMemberName("update")] Update,
}

/// <summary>
/// A notification-feed event shown by the TopBar bell and the Notificações screen — distinct from the
/// fixed <see cref="NotificationSetting"/> preferences. Raised by the run loop, the system monitor, etc.
/// </summary>
public class Notification
{
    public Guid Id { get; set; }
    public DateTimeOffset At { get; set; }
    public NotificationFeedKind Kind { get; set; }
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public bool Read { get; set; }

    /// <summary>Soft-delete tombstone. "Clear" hides feed entries (global query filter) instead of removing
    /// them; only the dev Master can list/restore/purge them.</summary>
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
}
