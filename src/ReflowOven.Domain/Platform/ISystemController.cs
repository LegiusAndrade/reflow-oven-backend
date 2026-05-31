using System.Text.Json.Serialization;

namespace ReflowOven.Domain.Platform;

/// <summary>How the device is connected to the LAN.</summary>
public enum NetworkLink
{
    [JsonStringEnumMemberName("Nenhum")] None,
    [JsonStringEnumMemberName("Cabo")] Cable,
    [JsonStringEnumMemberName("WiFi")] WiFi,
}

/// <summary>Live OS metrics for the Informação / Manutenção screens.</summary>
public readonly record struct SystemMetrics(
    string Hostname,
    string Os,
    string Kernel,
    long UptimeSeconds,
    double CpuLoadPercent,
    double? CpuTempC,
    long MemoryUsedMB,
    long MemoryTotalMB,
    double DiskFreeGB,
    double DiskTotalGB);

/// <summary>Current LAN connection (cable vs Wi-Fi, address, signal).</summary>
public readonly record struct NetworkStatus(
    NetworkLink Link,
    string Interface,
    string Ip,
    string? Ssid,
    int? SignalPercent,
    bool StaticIp);

/// <summary>A Wi-Fi network seen by a scan.</summary>
public readonly record struct WifiNetwork(string Ssid, int SignalPercent, bool Secured, bool Active);

/// <summary>Kind of a network interface (named to avoid colliding with <see cref="NetworkLink"/>).</summary>
public enum InterfaceKind
{
    [JsonStringEnumMemberName("Ethernet")] Ethernet,
    [JsonStringEnumMemberName("WiFi")] WiFi,
}

/// <summary>A network interface on the device (named <c>…Info</c> to avoid colliding with
/// <c>System.Net.NetworkInformation.NetworkInterface</c>).</summary>
public readonly record struct NetworkInterfaceInfo(string Name, InterfaceKind Kind, bool Up, string Ip);

/// <summary>Clock / NTP state.</summary>
public readonly record struct TimeStatus(DateTimeOffset Now, string Timezone, bool NtpSynchronized, bool NtpEnabled);

/// <summary>Installed vs available software version.</summary>
public readonly record struct UpdateStatus(string CurrentVersion, string? AvailableVersion, bool UpdateAvailable);

/// <summary>
/// Network settings to apply at the OS level (mirrors the Configurações → Rede form). Named
/// <c>OsNetworkConfig</c> to avoid colliding with the persisted <see cref="ReflowOven.Domain.Entities.NetworkConfig"/>.
/// </summary>
public sealed record OsNetworkConfig(
    bool StaticIp,
    string Ip,
    string Mask,
    string Gateway,
    string DnsPrimary,
    string DnsSecondary,
    NetworkLink PreferredLink);

/// <summary>
/// OS-level control of the OrangePi (network, clock, software update, power, connectivity) —
/// the device counterpart of <see cref="ReflowOven.Domain.Hardware.IPowerBoard"/>. A simulator
/// runs on a dev box; the real implementation shells out to nmcli/systemctl/etc. on the Pi
/// (selected by <c>System:Mode</c>). Nothing here touches the database.
/// </summary>
public interface ISystemController
{
    Task<SystemMetrics> GetMetricsAsync(CancellationToken ct = default);

    /// <summary>Instantaneous CPU utilization (%), averaged across all cores. On the Pi this is a short
    /// <c>/proc/stat</c> delta; the simulator returns a plausible value. Lighter than
    /// <see cref="GetMetricsAsync"/> when only the CPU figure is needed (e.g. the Manutenção overview).</summary>
    Task<double> GetCpuLoadPercentAsync(CancellationToken ct = default);

    Task<NetworkStatus> GetNetworkStatusAsync(CancellationToken ct = default);
    Task ApplyNetworkConfigAsync(OsNetworkConfig config, CancellationToken ct = default);
    Task<IReadOnlyList<WifiNetwork>> ScanWifiAsync(CancellationToken ct = default);
    Task ConnectWifiAsync(string ssid, string? password, CancellationToken ct = default);

    /// <summary>All network interfaces (cable/Wi-Fi, up/down, address).</summary>
    Task<IReadOnlyList<NetworkInterfaceInfo>> ListInterfacesAsync(CancellationToken ct = default);

    /// <summary>Make <paramref name="interfaceName"/> the preferred (highest-priority) interface.</summary>
    Task SetPriorityInterfaceAsync(string interfaceName, CancellationToken ct = default);

    Task<TimeStatus> GetTimeStatusAsync(CancellationToken ct = default);
    Task SetTimeAsync(DateTimeOffset time, CancellationToken ct = default);
    Task SetNtpAsync(bool enabled, CancellationToken ct = default);

    Task<UpdateStatus> GetUpdateStatusAsync(CancellationToken ct = default);

    /// <summary>Kick off the OTA/package update. Returns when the update has been launched (may reboot).</summary>
    Task ApplyUpdateAsync(CancellationToken ct = default);

    /// <summary>True when the configured central server is reachable (the "globe" indicator). Returns
    /// false when no central server is configured (an unconfigured server is treated as unreachable).</summary>
    Task<bool> PingCentralServerAsync(CancellationToken ct = default);

    Task RebootAsync(CancellationToken ct = default);
    Task ShutdownAsync(CancellationToken ct = default);
}
