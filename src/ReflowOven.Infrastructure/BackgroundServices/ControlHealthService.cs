using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ReflowOven.Infrastructure.Persistence;
using ReflowOven.Infrastructure.Platform;

namespace ReflowOven.Infrastructure.BackgroundServices;

/// <summary>
/// Samples the CONTROL board's own health into <see cref="IControlHealth"/> (which drives the STATUS LED
/// blink code), distinct from the power board's protection faults. Power-good (GPIO) and the RS422 link are
/// read live every second; the heavier checks — database, front (Next.js), central server, disk and the
/// clock — run every few seconds. Every signal is best-effort and degrades to "healthy" so a probe glitch
/// never flashes a false fault. No-op-ish under the simulator (GPIO good, link up, DB the dev one, etc.).
/// </summary>
public sealed class ControlHealthService(
    IControlHealth health,
    IBoardGpio gpio,
    IPowerBoard board,
    ISystemController system,
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpFactory,
    IOptions<SystemOptions> options,
    ILogger<ControlHealthService> logger) : BackgroundService
{
    private readonly SystemOptions _o = options.Value;
    private const int SlowEveryTicks = 5; // the heavy checks run every 5 s (the tick is 1 s)

    // Last results of the slow checks, re-used on the in-between ticks (default healthy).
    private bool _dbUp = true, _frontUp = true, _centralUp = true, _diskOk = true, _clockOk = true;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        var tick = 0;
        try
        {
            await RunSlowChecksAsync(ct); // prime before the first publish so the LED never flashes a false fault
            do
            {
                if (tick % SlowEveryTicks == 0) await RunSlowChecksAsync(ct);
                health.Set(new ControlHealthSnapshot(
                    PowerGood: gpio.ReadPowerGood(),
                    PowerLinkUp: board.IsConnected,
                    DatabaseUp: _dbUp,
                    FrontUp: _frontUp,
                    CentralUp: _centralUp,
                    DiskOk: _diskOk,
                    ClockSynced: _clockOk));
                tick++;
            }
            while (await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown — the stopping token cancels a check or WaitForNextTickAsync.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha no monitor de saúde do controle.");
        }
    }

    private async Task RunSlowChecksAsync(CancellationToken ct)
    {
        _dbUp = await CheckDatabaseAsync(ct);
        _frontUp = await CheckFrontAsync(ct);
        // PingCentralServerAsync returns false when no server is configured — treat "unconfigured" as OK.
        _centralUp = string.IsNullOrWhiteSpace(_o.CentralServerUrl) || await system.PingCentralServerAsync(ct);
        _diskOk = CheckDisk();
        _clockOk = await CheckClockAsync(ct);
    }

    private async Task<bool> CheckDatabaseAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ReflowDbContext>();
            return await db.Database.CanConnectAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Saúde: banco inacessível.");
            return false;
        }
    }

    private async Task<bool> CheckFrontAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_o.FrontUrl)) return true; // not configured → not a fault (e.g. dev box)
        try
        {
            using var http = httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(3);
            // Any HTTP response (even 404) means the Next.js server is up; only a refused/timed-out connection is "down".
            using var resp = await http.GetAsync(_o.FrontUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Saúde: front inacessível ({Url}).", _o.FrontUrl);
            return false;
        }
    }

    private bool CheckDisk()
    {
        try
        {
            var root = new DriveInfo(Path.GetPathRoot(AppContext.BaseDirectory) is { Length: > 0 } r ? r : "/");
            var freePct = root.TotalSize > 0 ? (double)root.AvailableFreeSpace / root.TotalSize * 100 : 100;
            return freePct >= DomainConstants.DiskLowFreePercent;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Saúde: leitura de disco falhou.");
            return true;
        }
    }

    private async Task<bool> CheckClockAsync(CancellationToken ct)
    {
        try
        {
            var t = await system.GetTimeStatusAsync(ct);
            // The appliance keeps wall time on a battery-backed RTC (PT7C4339): when it is bound, boot time
            // was restored from it and an unsynced NTP (an offline bench) does NOT mean the clock is wrong.
            // Only flag when there is no RTC to fall back on AND NTP is enabled but hasn't synced yet.
            return t.RtcPresent || !(t.NtpEnabled && !t.NtpSynchronized);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Saúde: leitura do relógio falhou.");
            return true;
        }
    }
}
