namespace ReflowOven.Application.Dtos;

/// <summary>Live OS metrics (Informação / Sistema screens). Maps <see cref="SystemMetrics"/>.</summary>
public sealed record SystemMetricsDto(
    string Hostname,
    string Os,
    string Kernel,
    long UptimeSeconds,
    double CpuLoadPercent,
    double? CpuTempC,
    long MemoryUsedMB,
    long MemoryTotalMB,
    double DiskFreeGB,
    double DiskTotalGB)
{
    public static SystemMetricsDto From(SystemMetrics m) =>
        new(m.Hostname, m.Os, m.Kernel, m.UptimeSeconds, m.CpuLoadPercent, m.CpuTempC,
            m.MemoryUsedMB, m.MemoryTotalMB, m.DiskFreeGB, m.DiskTotalGB);
}

/// <summary>Current LAN connection. <c>Link</c> serializes to "Nenhum"/"Cabo"/"WiFi".</summary>
public sealed record NetworkStatusDto(
    NetworkLink Link,
    string Interface,
    string Ip,
    string? Ssid,
    int? SignalPercent,
    bool StaticIp)
{
    public static NetworkStatusDto From(NetworkStatus n) =>
        new(n.Link, n.Interface, n.Ip, n.Ssid, n.SignalPercent, n.StaticIp);
}

public sealed record WifiNetworkDto(string Ssid, int SignalPercent, bool Secured, bool Active)
{
    public static WifiNetworkDto From(WifiNetwork w) => new(w.Ssid, w.SignalPercent, w.Secured, w.Active);
}

/// <summary>A network interface (cable/Wi-Fi, up/down, address). <c>Kind</c> serializes to "Ethernet"/"WiFi".</summary>
public sealed record NetworkInterfaceDto(string Name, InterfaceKind Kind, bool Up, string Ip)
{
    public static NetworkInterfaceDto From(NetworkInterfaceInfo i) => new(i.Name, i.Kind, i.Up, i.Ip);
}

public sealed record TimeStatusDto(DateTimeOffset Now, string Timezone, bool NtpSynchronized, bool NtpEnabled)
{
    public static TimeStatusDto From(TimeStatus t) => new(t.Now, t.Timezone, t.NtpSynchronized, t.NtpEnabled);
}

public sealed record UpdateStatusDto(string CurrentVersion, string? AvailableVersion, bool UpdateAvailable)
{
    public static UpdateStatusDto From(UpdateStatus u) => new(u.CurrentVersion, u.AvailableVersion, u.UpdateAvailable);
}

/// <summary>Central-server reachability (the TopBar globe indicator).</summary>
public sealed record ConnectivityDto(bool Online);

/// <summary>Everything the Sistema dashboard needs in a single round-trip.</summary>
public sealed record SystemStatusDto(
    SystemMetricsDto Metrics,
    NetworkStatusDto Network,
    TimeStatusDto Time,
    UpdateStatusDto Update,
    bool CentralServerOnline,
    long DatabaseSizeBytes);

// --- requests ---------------------------------------------------------------------------------

/// <summary>Apply network settings at the OS level (Configurações → Rede). Mirrors <see cref="NetworkDto"/>
/// plus the preferred link.</summary>
public sealed record ApplyNetworkRequest(
    bool StaticIp,
    string Ip,
    string Mask,
    string Gateway,
    string DnsPrimary,
    string DnsSecondary,
    NetworkLink PreferredLink);

public sealed record ConnectWifiRequest(string Ssid, string? Password);

public sealed record SetPriorityInterfaceRequest(string InterfaceName);

public sealed record SetTimeRequest(DateTimeOffset Time);

public sealed record SetNtpRequest(bool Enabled);
