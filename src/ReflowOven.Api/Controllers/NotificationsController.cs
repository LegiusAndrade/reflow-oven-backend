namespace ReflowOven.Api.Controllers;

/// <summary>Notification feed for the TopBar bell and the Notificações screen.</summary>
[ApiController]
[Route("api/notifications")]
public sealed class NotificationsController(NotificationService notifications) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<NotificationDto>> List(
        [FromQuery] bool unreadOnly = false,
        [FromQuery] int limit = DomainConstants.NotificationFeedMax,
        CancellationToken ct = default)
        => notifications.ListAsync(unreadOnly, limit, ct);

    [HttpGet("unread-count")]
    public async Task<UnreadCountDto> UnreadCount(CancellationToken ct) =>
        new(await notifications.UnreadCountAsync(ct));

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct)
    {
        await notifications.MarkReadAsync(id, ct);
        return NoContent();
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct)
    {
        await notifications.MarkAllReadAsync(ct);
        return NoContent();
    }

    [Authorize(Policy = AuthPolicies.AdminOnly)]
    [HttpDelete]
    public async Task<IActionResult> Clear(CancellationToken ct)
    {
        await notifications.ClearAsync(ct);
        return NoContent();
    }

    // --- Master "trash" (soft-deleted feed entries): only the dev Master may view/restore/purge ------
    [Authorize(Policy = AuthPolicies.MasterOnly)]
    [HttpGet("deleted")]
    public Task<IReadOnlyList<DeletedNotificationDto>> ListDeleted(CancellationToken ct) => notifications.ListDeletedAsync(ct);

    [Authorize(Policy = AuthPolicies.MasterOnly)]
    [HttpPost("{id:guid}/restore")]
    public async Task<IActionResult> Restore(Guid id, CancellationToken ct)
    {
        await notifications.RestoreAsync(id, ct);
        return NoContent();
    }

    [Authorize(Policy = AuthPolicies.MasterOnly)]
    [HttpDelete("{id:guid}/purge")]
    public async Task<IActionResult> Purge(Guid id, CancellationToken ct)
    {
        await notifications.PurgeAsync(id, ct);
        return NoContent();
    }
}
