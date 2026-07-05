using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ReflowOven.Application.Dtos;
using ReflowOven.Application.Services;
using ReflowOven.Infrastructure.Platform;

namespace ReflowOven.Infrastructure.BackgroundServices;

/// <summary>
/// Polls central-server reachability and the OTA update status; on a state change it appends a
/// notification-feed entry (the TopBar bell). Harmless under <c>System:Mode=Simulated</c> — the
/// simulator reports "online" and no update, so nothing is ever raised. The first poll only
/// establishes the baseline (no notification). Interval = <c>System:MonitorIntervalSeconds</c>.
/// Also the home of the daily data-retention sweep (see <see cref="RetentionOptions"/>).
/// </summary>
public sealed class SystemMonitorService(
    ISystemController system,
    IBoardGpio gpio,
    IServiceScopeFactory scopeFactory,
    IClock clock,
    IEmailSender email,
    ISystemLogSink systemLog,
    INotificationSink notifications,
    IOptions<SystemOptions> options,
    IOptions<RetentionOptions> retention,
    ILogger<SystemMonitorService> logger) : BackgroundService
{
    private bool? _lastOnline;
    private string? _lastNotifiedVersion;
    private bool? _lastDiskLow;
    private bool? _lastPowerGood;

    /// <summary>Monotonic mark of the last retention sweep; 0 = never (sweep on the first poll after
    /// boot, so a device rebooted daily still gets its retention applied).</summary>
    private long _lastRetentionSweep;
    private static readonly TimeSpan RetentionSweepPeriod = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var period = TimeSpan.FromSeconds(Math.Max(10, options.Value.MonitorIntervalSeconds));
        using var timer = new PeriodicTimer(period);
        try
        {
            do
            {
                try
                {
                    await PollAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    throw; // shutdown: bubble to the outer handler so the loop ends cleanly
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Falha no monitor de sistema (servidor central / atualização).");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown — the stopping token cancels PollAsync or WaitForNextTickAsync. Swallow it
            // so it isn't surfaced as unhandled (the debugger was flagging this) and the service stops cleanly.
        }
    }

    private async Task PollAsync(CancellationToken ct)
    {
        var online = await system.PingCentralServerAsync(ct);
        if (_lastOnline is bool prev && prev != online)
        {
            await RaiseAsync(
                online ? NotificationFeedKind.Info : NotificationFeedKind.Error,
                online ? "Servidor central reconectado" : "Servidor central inacessível",
                online
                    ? "A conexão com o servidor central foi restabelecida."
                    : "O dispositivo perdeu a conexão com o servidor central.",
                ct);
            await RecordCommAsync(OperationObject.Controlador, "servidor-central",
                [OperationField.Of("estado", online ? "online" : "offline")], ct);
        }
        _lastOnline = online;

        var update = await system.GetUpdateStatusAsync(ct);
        if (update is { UpdateAvailable: true, AvailableVersion: { Length: > 0 } v })
        {
            // _lastNotifiedVersion is in-memory and null after a restart; seed it from the feed so a pending
            // update that was already announced isn't re-announced every time the API restarts.
            if (_lastNotifiedVersion is null && await AlreadyNotifiedAsync(v, ct))
                _lastNotifiedVersion = v;

            if (v != _lastNotifiedVersion)
            {
                await RaiseAsync(NotificationFeedKind.Update, "Atualização disponível",
                    $"Nova versão {v} disponível para instalação.", ct);
                await RecordCommAsync(OperationObject.Sistema, "ota", [OperationField.Of("versao", v)], ct);
                _lastNotifiedVersion = v;
            }
        }

        // Low-disk alert: fire only when crossing into the low state (not on the first poll baseline).
        var metrics = await system.GetMetricsAsync(ct);
        if (metrics.DiskTotalGB > 0)
        {
            var freePct = metrics.DiskFreeGB / metrics.DiskTotalGB * 100;
            var low = freePct < DomainConstants.DiskLowFreePercent;
            if (low && _lastDiskLow == false)
                await RaiseDiskLowAsync(freePct, metrics.DiskFreeGB, metrics.DiskTotalGB, ct);
            _lastDiskLow = low;
        }

        // Power-good: notify on a transition (skip the first-poll baseline). Polled at the monitor cadence —
        // for a faster reaction a GPIO edge interrupt could drive this instead. No-op under Simulated (always good).
        var powerGood = gpio.ReadPowerGood();
        if (_lastPowerGood is bool prevPg && prevPg != powerGood)
        {
            await RaiseAsync(
                powerGood ? NotificationFeedKind.Info : NotificationFeedKind.Error,
                powerGood ? "Alimentação restabelecida" : "Falha de alimentação",
                powerGood
                    ? "O sinal de power-good foi restabelecido."
                    : "O sinal de power-good caiu — verifique a alimentação do equipamento.",
                ct);
            await RecordCommAsync(OperationObject.Sistema, "power-good",
                [OperationField.Of("estado", powerGood ? "ok" : "falha")], ct);
        }
        _lastPowerGood = powerGood;

        await SweepRetentionAsync(ct);
    }

    /// <summary>
    /// Daily retention janitor for the append-only stores the Manutenção cleanup deliberately refuses to
    /// clear: purges operation-log rows past <see cref="RetentionOptions.OperationLogDays"/> and emptied
    /// (soft-deleted) notifications past <see cref="RetentionOptions.NotificationTrashDays"/>. Scheduled on
    /// the monotonic clock (a wall-clock step never skips or double-runs it); best-effort — a failure is
    /// logged and retried at the next 24 h window (retention is a janitor, not a time-critical task). The
    /// sweep is audited on the operation log itself (after the purge, so the fresh row survives it).
    /// </summary>
    private async Task SweepRetentionAsync(CancellationToken ct)
    {
        var o = retention.Value;
        if (o.OperationLogDays <= 0 && o.NotificationTrashDays <= 0) return; // fully disabled
        if (_lastRetentionSweep != 0 && clock.GetElapsedTime(_lastRetentionSweep) < RetentionSweepPeriod) return;
        _lastRetentionSweep = clock.GetTimestamp();

        try
        {
            var now = clock.UtcNow;
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();

            var opLogRemoved = 0;
            if (o.OperationLogDays > 0)
            {
                var cutoff = now.AddDays(-o.OperationLogDays);
                opLogRemoved = await db.OperationLog.Where(x => x.At < cutoff).ExecuteDeleteAsync(ct);
            }

            var trashRemoved = 0;
            if (o.NotificationTrashDays > 0)
            {
                var cutoff = now.AddDays(-o.NotificationTrashDays);
                trashRemoved = await db.Notifications.IgnoreQueryFilters()
                    .Where(n => n.IsDeleted && n.DeletedAt != null && n.DeletedAt < cutoff)
                    .ExecuteDeleteAsync(ct);
            }

            if (opLogRemoved > 0 || trashRemoved > 0)
            {
                var audit = scope.ServiceProvider.GetRequiredService<AuditService>();
                audit.Record(OperationType.Limpeza, OperationObject.Sistema, "retencao",
                    [
                        OperationField.Of("log_operacao_removidos", opLogRemoved),
                        OperationField.Of("lixeira_notificacoes_removidas", trashRemoved),
                    ],
                    operatorName: "Sistema");
                await db.SaveChangesAsync(ct);
                logger.LogInformation(
                    "Retenção aplicada: {OpLog} linha(s) do log de operação e {Trash} notificação(ões) da lixeira removidas.",
                    opLogRemoved, trashRemoved);
            }

            // BE-6 purge preview: announce what TOMORROW's sweep will delete via ONE refreshed feed entry.
            var warning = await UpsertPurgeWarningAsync(db, o, now, ct);
            if (warning is not null)
            {
                await notifications.PublishAsync(warning);
                logger.LogInformation("Aviso de expurgo de retenção atualizado: {Message}", warning.Message);
            }
        }
        catch (OperationCanceledException)
        {
            throw; // shutdown: bubble to the poll loop's handler
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha na varredura de retenção (nova tentativa na próxima janela de 24 h).");
        }
    }

    /// <summary>Title of THE single pending purge-preview warning — also its upsert key in the feed.</summary>
    internal const string PurgeWarningTitle = "Exclusão de registros agendada";

    /// <summary>
    /// BE-6 purge preview: computes what the NEXT daily sweep will delete — rows crossing their retention
    /// threshold within 24 h, i.e. already older than <c>cutoff − 1 day</c> — and upserts ONE Warning feed
    /// entry (the feed's "Atenção" level), refreshed in place instead of stacking a new row per day. When
    /// operation-log rows are involved it carries a Relatórios deep link
    /// (<c>{ tab: "relatorios", until: cutoff }</c>); a trash-only warning has none (the notification trash
    /// is Master-only, no Relatórios view applies). Returns the DTO to publish, or null when nothing will
    /// be deleted or the pending entry already tells today's picture. Static + internal so the unit tests
    /// can drive it directly against an in-memory context.
    /// </summary>
    internal static async Task<NotificationDto?> UpsertPurgeWarningAsync(
        IAppDbContext db, RetentionOptions options, DateTimeOffset now, CancellationToken ct)
    {
        var opCutoff = now.AddDays(1 - options.OperationLogDays);       // what tomorrow's sweep will use
        var trashCutoff = now.AddDays(1 - options.NotificationTrashDays);

        var opCount = options.OperationLogDays > 0
            ? await db.OperationLog.CountAsync(x => x.At < opCutoff, ct)
            : 0;
        var trashCount = options.NotificationTrashDays > 0
            ? await db.Notifications.IgnoreQueryFilters()
                .CountAsync(n => n.IsDeleted && n.DeletedAt != null && n.DeletedAt < trashCutoff, ct)
            : 0;
        if (opCount == 0 && trashCount == 0) return null;

        var message = ComposePurgeWarningMessage(opCount, opCutoff, trashCount, trashCutoff);
        var pending = await db.Notifications.FirstOrDefaultAsync(n => n.Title == PurgeWarningTitle, ct);
        if (pending is not null && pending.Message == message) return null; // today's picture is already told

        if (pending is null)
        {
            pending = new Notification { Id = Guid.NewGuid(), Title = PurgeWarningTitle };
            db.Notifications.Add(pending);
        }
        pending.Kind = NotificationFeedKind.Warning;
        pending.At = now;
        pending.Message = message;
        pending.Read = false; // refreshed content re-surfaces on the bell
        pending.DeepLink = opCount > 0 ? new NotificationDeepLink { Tab = "relatorios", Until = opCutoff } : null;
        await db.SaveChangesAsync(ct);
        return NotificationDto.From(pending);
    }

    /// <summary>The pt-BR warning line — "N … serão excluídos amanhã (anteriores a DD/MM/AAAA)." — with
    /// each category keeping its own cutoff date. Dates are pinned to DD/MM/AAAA whatever the host culture.</summary>
    internal static string ComposePurgeWarningMessage(
        int opCount, DateTimeOffset opCutoff, int trashCount, DateTimeOffset trashCutoff)
    {
        var op = $"{opCount} registro(s) do log de operação";
        var trash = $"{trashCount} notificação(ões) da lixeira";
        return (opCount > 0, trashCount > 0) switch
        {
            (true, false) => $"{op} serão excluídos amanhã (anteriores a {D(opCutoff)}).",
            (false, true) => $"{trash} serão excluídas amanhã (anteriores a {D(trashCutoff)}).",
            _ => $"{op} (anteriores a {D(opCutoff)}) e {trash} (anteriores a {D(trashCutoff)}) serão excluídos amanhã.",
        };

        static string D(DateTimeOffset d) =>
            d.ToString("dd'/'MM'/'yyyy", System.Globalization.CultureInfo.InvariantCulture);
    }

    private async Task RaiseDiskLowAsync(double freePct, double freeGB, double totalGB, CancellationToken ct)
    {
        var message = $"Espaço livre em disco em {freePct:F0}% ({freeGB:F1} GB de {totalGB:F1} GB). Considere limpar dados antigos.";
        await RaiseAsync(NotificationFeedKind.Error, "Espaço em disco crítico", message, ct);

        // E-mail the active admins (best-effort — a failure must not stop the monitor).
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            var admins = await db.Users
                .Where(u => u.Type == UserType.Admin && u.Status == UserStatus.Ativo)
                .Select(u => u.Email).ToListAsync(ct);
            await email.SendDiskLowAsync(admins, freePct, freeGB, totalGB, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao enviar o alerta de disco baixo por e-mail.");
        }
    }

    private async Task<bool> AlreadyNotifiedAsync(string version, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        // Delimited match so a "1.2" version isn't considered already-announced by a "1.20" message.
        var needle = $"versão {version} ";
        return await db.Notifications.AnyAsync(
            n => n.Kind == NotificationFeedKind.Update && n.Message.Contains(needle), ct);
    }

    private async Task RaiseAsync(NotificationFeedKind kind, string title, string message, CancellationToken ct)
    {
        var at = clock.UtcNow;
        // Mirror the notification onto the Log do Sistema: an update is informational, anything else a warning.
        var logEntry = new SystemLogEntry
        {
            At = at,
            // Fully-qualified: this file imports Microsoft.Extensions.Logging, whose LogLevel would collide.
            Level = kind == NotificationFeedKind.Update ? ReflowOven.Domain.Enums.LogLevel.Info : ReflowOven.Domain.Enums.LogLevel.Aviso,
            Message = $"{title}: {message}",
        };

        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            var notification = new Notification
            {
                Id = Guid.NewGuid(),
                At = at,
                Kind = kind,
                Title = title,
                Message = message,
            };
            db.Notifications.Add(notification);
            db.SystemLog.Add(logEntry); // same unit of work; Id populated by the save below.
            await db.SaveChangesAsync(ct);
            await notifications.PublishAsync(ReflowOven.Application.Dtos.NotificationDto.From(notification));
        }

        logger.LogInformation("Notificação gerada: {Kind} — {Title}", kind, title);
        await systemLog.PublishAsync(new ReflowOven.Application.Dtos.SystemLogDto(logEntry.Id, logEntry.At, logEntry.Level, logEntry.Message));
    }

    /// <summary>Append one "Sistema"-operator <c>Comunicacao</c> row to the universal operation log (central
    /// server link changes, OTA availability). Best-effort: a failure must not stop the monitor loop.</summary>
    private async Task RecordCommAsync(OperationObject obj, string objectId, IReadOnlyList<OperationField> fields, CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var audit = scope.ServiceProvider.GetRequiredService<AuditService>();
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            audit.Record(OperationType.Comunicacao, obj, objectId, fields, operatorName: "Sistema");
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao auditar evento de comunicação ({Object}/{ObjectId}).", obj, objectId);
        }
    }
}
