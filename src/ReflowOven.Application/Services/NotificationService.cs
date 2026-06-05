namespace ReflowOven.Application.Services;

/// <summary>
/// Backs the notification feed (TopBar bell + Notificações screen): list / unread-count / mark-read,
/// plus <see cref="RaiseAsync"/> used by the run loop (execution finished) and the system monitor
/// (central server up/down, update available). Feed entries are device-wide, not per-user.
/// </summary>
public sealed class NotificationService(IAppDbContext db, IClock clock, ICurrentUser current)
{
    /// <summary>Most recent feed entries (newest first), server-clamped to <see cref="DomainConstants.NotificationFeedMax"/>.</summary>
    public async Task<IReadOnlyList<NotificationDto>> ListAsync(
        bool unreadOnly = false, int limit = DomainConstants.NotificationFeedMax, CancellationToken ct = default)
    {
        var take = Math.Clamp(limit, 1, DomainConstants.NotificationFeedMax);
        var q = db.Notifications.AsNoTracking();
        if (unreadOnly) q = q.Where(n => !n.Read);
        var rows = await q.OrderByDescending(n => n.At).Take(take).ToListAsync(ct);
        return rows.Select(NotificationDto.From).ToList();
    }

    public Task<int> UnreadCountAsync(CancellationToken ct = default) =>
        db.Notifications.CountAsync(n => !n.Read, ct);

    public async Task MarkReadAsync(Guid id, CancellationToken ct = default)
    {
        var n = await db.Notifications.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException("Notificação não encontrada.");
        if (n.Read) return;
        n.Read = true;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Marks every unread entry as read (called when the user leaves the Notificações screen).</summary>
    public Task<int> MarkAllReadAsync(CancellationToken ct = default) =>
        db.Notifications.Where(n => !n.Read).ExecuteUpdateAsync(s => s.SetProperty(n => n.Read, true), ct);

    /// <summary>Appends a feed entry and returns it. Best-effort callers should swallow failures.</summary>
    public async Task<NotificationDto> RaiseAsync(NotificationFeedKind kind, string title, string message, CancellationToken ct = default)
    {
        var n = new Notification
        {
            Id = Guid.NewGuid(),
            At = clock.UtcNow,
            Kind = kind,
            Title = title,
            Message = message,
            Read = false,
        };
        db.Notifications.Add(n);
        await db.SaveChangesAsync(ct);
        return NotificationDto.From(n);
    }

    /// <summary>Soft-delete the visible feed (hides it); only the Master can list/restore/purge it afterwards.</summary>
    public Task<int> ClearAsync(CancellationToken ct = default) =>
        db.Notifications.ExecuteUpdateAsync(s => s
            .SetProperty(n => n.IsDeleted, true)
            .SetProperty(n => n.DeletedAt, clock.UtcNow)
            .SetProperty(n => n.DeletedBy, current.Name), ct);

    /// <summary>Master "trash": soft-deleted feed entries (newest-deleted first), capped to the feed max.</summary>
    public async Task<IReadOnlyList<DeletedNotificationDto>> ListDeletedAsync(CancellationToken ct = default)
    {
        var rows = await db.Notifications.IgnoreQueryFilters().AsNoTracking()
            .Where(n => n.IsDeleted)
            .OrderByDescending(n => n.DeletedAt)
            .Take(DomainConstants.NotificationFeedMax)
            .ToListAsync(ct);
        return rows.Select(n => new DeletedNotificationDto(n.Id.ToString(), n.Kind, n.At, n.Title, n.Message, n.DeletedAt, n.DeletedBy)).ToList();
    }

    /// <summary>Master action: restore a soft-deleted feed entry.</summary>
    public async Task RestoreAsync(Guid id, CancellationToken ct = default)
    {
        var n = await db.Notifications.IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Id == id && x.IsDeleted, ct)
            ?? throw new NotFoundException("Notificação apagada não encontrada.");
        n.IsDeleted = false;
        n.DeletedAt = null;
        n.DeletedBy = null;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Master action: permanently delete a soft-deleted feed entry (irreversible).</summary>
    public async Task PurgeAsync(Guid id, CancellationToken ct = default)
    {
        var n = await db.Notifications.IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Id == id && x.IsDeleted, ct)
            ?? throw new NotFoundException("Notificação apagada não encontrada.");
        db.Notifications.Remove(n);
        await db.SaveChangesAsync(ct);
    }
}
