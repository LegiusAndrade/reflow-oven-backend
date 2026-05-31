using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ReflowOven.Domain.Platform;

namespace ReflowOven.Infrastructure.Platform;

/// <summary>
/// Default controller for a dev box (no OrangePi): returns plausible OS metrics/network/time and
/// treats every mutation as a logged no-op. Mirrors the <see cref="SimulatedPowerBoard"/> philosophy
/// so the whole API runs and the frontend integrates with no hardware. Selected unless <c>System:Mode=Linux</c>.
/// </summary>
public sealed class SimulatedSystemController(IClock clock, IOptions<SystemOptions> options, ILogger<SimulatedSystemController> logger) : ISystemController
{
    private readonly SystemOptions _o = options.Value;
    private bool _wifi;          // pretend we're on cable until someone "connects" Wi-Fi
    private bool _ntp = true;
    private bool _staticIp;

    public Task<SystemMetrics> GetMetricsAsync(CancellationToken ct = default) =>
        Task.FromResult(new SystemMetrics(
            Hostname: "reflow-oven",
            Os: "Armbian (simulado)",
            Kernel: "6.6.0-edge-rockchip64",
            UptimeSeconds: 3 * 3600 + Random.Shared.Next(0, 3600),
            CpuLoadPercent: Math.Round(8 + Random.Shared.NextDouble() * 20, 1),
            CpuTempC: Math.Round(42 + Random.Shared.NextDouble() * 8, 1),
            MemoryUsedMB: 420 + Random.Shared.Next(0, 120),
            MemoryTotalMB: 2048,
            DiskFreeGB: Math.Round(22 + Random.Shared.NextDouble(), 1),
            DiskTotalGB: 32));

    public Task<NetworkStatus> GetNetworkStatusAsync(CancellationToken ct = default) =>
        Task.FromResult(_wifi
            ? new NetworkStatus(NetworkLink.WiFi, _o.WifiInterface, "192.168.0.42", "Reflow-Lab", 72, _staticIp)
            : new NetworkStatus(NetworkLink.Cable, _o.EthernetInterface, "192.168.0.42", null, null, _staticIp));

    public Task ApplyNetworkConfigAsync(OsNetworkConfig config, CancellationToken ct = default)
    {
        _staticIp = config.StaticIp;
        _wifi = config.PreferredLink == NetworkLink.WiFi;
        logger.LogInformation("[sim] Rede aplicada: {Link}, {Mode} {Ip}", config.PreferredLink, config.StaticIp ? "estático" : "DHCP", config.Ip);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<WifiNetwork>> ScanWifiAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<WifiNetwork>>(
        [
            new("Reflow-Lab", 72, true, _wifi),
            new("Fabrica-2G", 55, true, false),
            new("Visitantes", 38, false, false),
        ]);

    public Task ConnectWifiAsync(string ssid, string? password, CancellationToken ct = default)
    {
        _wifi = true;
        logger.LogInformation("[sim] Conectando ao Wi-Fi '{Ssid}'.", ssid);
        return Task.CompletedTask;
    }

    public Task<TimeStatus> GetTimeStatusAsync(CancellationToken ct = default) =>
        Task.FromResult(new TimeStatus(clock.UtcNow, "America/Sao_Paulo", _ntp, _ntp));

    public Task SetTimeAsync(DateTimeOffset time, CancellationToken ct = default)
    {
        logger.LogInformation("[sim] Hora ajustada para {Time:o}.", time);
        return Task.CompletedTask;
    }

    public Task SetNtpAsync(bool enabled, CancellationToken ct = default)
    {
        _ntp = enabled;
        logger.LogInformation("[sim] NTP {State}.", enabled ? "ativado" : "desativado");
        return Task.CompletedTask;
    }

    public Task<UpdateStatus> GetUpdateStatusAsync(CancellationToken ct = default) =>
        Task.FromResult(new UpdateStatus(_o.CurrentVersion, null, false));

    public Task ApplyUpdateAsync(CancellationToken ct = default)
    {
        logger.LogInformation("[sim] Atualização de software solicitada (no-op).");
        return Task.CompletedTask;
    }

    public Task<bool> PingCentralServerAsync(CancellationToken ct = default) => Task.FromResult(true);

    public Task RebootAsync(CancellationToken ct = default)
    {
        logger.LogWarning("[sim] Reboot solicitado (no-op).");
        return Task.CompletedTask;
    }

    public Task ShutdownAsync(CancellationToken ct = default)
    {
        logger.LogWarning("[sim] Shutdown solicitado (no-op).");
        return Task.CompletedTask;
    }
}
