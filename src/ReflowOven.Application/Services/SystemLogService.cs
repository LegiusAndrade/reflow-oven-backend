namespace ReflowOven.Application.Services;

/// <summary>
/// Writes a line to the system log (Log do Sistema) AND pushes it live to the SystemLog hub. Use this
/// from Application-layer callers that own a unit of work of their own. Background services that already
/// batch their own <c>SaveChangesAsync</c> (RunManager, SystemMonitorService) instead add the
/// <see cref="SystemLogEntry"/> to their existing context and push via <see cref="ISystemLogSink"/>
/// directly, to avoid a second commit.
/// </summary>
public sealed class SystemLogService(IAppDbContext db, IClock clock, ISystemLogSink sink)
{
    public async Task WriteAsync(LogLevel level, string message, CancellationToken ct = default)
    {
        var entry = new SystemLogEntry { At = clock.UtcNow, Level = level, Message = message };
        db.SystemLog.Add(entry);
        await db.SaveChangesAsync(ct); // Id is identity-generated here, so the pushed DTO carries the real id.
        await sink.PublishAsync(new SystemLogDto(entry.Id, entry.At, entry.Level, entry.Message));
    }
}
