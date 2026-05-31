using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ReflowOven.Infrastructure.Platform;

namespace ReflowOven.Infrastructure.BackgroundServices;

/// <summary>
/// Polls central-server reachability and the OTA update status; on a state change it appends a
/// notification-feed entry (the TopBar bell). Harmless under <c>System:Mode=Simulated</c> — the
/// simulator reports "online" and no update, so nothing is ever raised. The first poll only
/// establishes the baseline (no notification). Interval = <c>System:MonitorIntervalSeconds</c>.
/// </summary>
public sealed class SystemMonitorService(
    ISystemController system,
    IServiceScopeFactory scopeFactory,
    IClock clock,
    IOptions<SystemOptions> options,
    ILogger<SystemMonitorService> logger) : BackgroundService
{
    private bool? _lastOnline;
    private string? _lastNotifiedVersion;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var period = TimeSpan.FromSeconds(Math.Max(10, options.Value.MonitorIntervalSeconds));
        using var timer = new PeriodicTimer(period);
        do
        {
            try
            {
                await PollAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha no monitor de sistema (servidor central / atualização).");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task PollAsync(CancellationToken ct)
    {
        var online = await system.PingCentralServerAsync(ct);
        if (_lastOnline is bool prev && prev != online)
            await RaiseAsync(
                online ? NotificationFeedKind.Info : NotificationFeedKind.Error,
                online ? "Servidor central reconectado" : "Servidor central inacessível",
                online
                    ? "A conexão com o servidor central foi restabelecida."
                    : "O dispositivo perdeu a conexão com o servidor central.",
                ct);
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
                _lastNotifiedVersion = v;
            }
        }
    }

    private async Task<bool> AlreadyNotifiedAsync(string version, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        return await db.Notifications.AnyAsync(
            n => n.Kind == NotificationFeedKind.Update && n.Message.Contains(version), ct);
    }

    private async Task RaiseAsync(NotificationFeedKind kind, string title, string message, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        db.Notifications.Add(new Notification
        {
            Id = Guid.NewGuid(),
            At = clock.UtcNow,
            Kind = kind,
            Title = title,
            Message = message,
        });
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Notificação gerada: {Kind} — {Title}", kind, title);
    }
}
