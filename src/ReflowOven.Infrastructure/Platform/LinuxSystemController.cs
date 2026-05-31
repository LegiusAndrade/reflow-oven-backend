using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ReflowOven.Domain.Platform;

namespace ReflowOven.Infrastructure.Platform;

/// <summary>
/// Real OrangePi controller (selected by <c>System:Mode=Linux</c>). Reads /proc + /sys for metrics
/// and shells out to <c>nmcli</c> / <c>timedatectl</c> / <c>systemctl</c> for network, clock and power.
/// Mutating commands need privileges — run the API under a user with the matching sudoers/polkit rules
/// (see the GUIA). Every command is best-effort: failures are logged and degrade gracefully.
/// </summary>
public sealed class LinuxSystemController(
    IClock clock,
    IOptions<SystemOptions> options,
    IHttpClientFactory httpFactory,
    ILogger<LinuxSystemController> logger) : ISystemController
{
    private readonly SystemOptions _o = options.Value;

    /// <summary>Sampling window for the /proc/stat CPU-utilization delta (ms).</summary>
    private const int CpuSampleMs = 200;

    public async Task<SystemMetrics> GetMetricsAsync(CancellationToken ct = default)
    {
        var (memUsed, memTotal) = ReadMemoryMB();
        var root = new DriveInfo(Path.GetPathRoot(AppContext.BaseDirectory) is { Length: > 0 } r ? r : "/");
        return new SystemMetrics(
            Hostname: Environment.MachineName,
            Os: ReadOsPrettyName(),
            Kernel: (await ProcessRunner.RunAsync("uname", "-r", ct)).StdOut is { Length: > 0 } k ? k : Environment.OSVersion.VersionString,
            UptimeSeconds: ReadUptimeSeconds(),
            CpuLoadPercent: await GetCpuLoadPercentAsync(ct),
            CpuTempC: ReadCpuTempC(),
            MemoryUsedMB: memUsed,
            MemoryTotalMB: memTotal,
            DiskFreeGB: Math.Round(root.AvailableFreeSpace / 1e9, 1),
            DiskTotalGB: Math.Round(root.TotalSize / 1e9, 1));
    }

    public async Task<double> GetCpuLoadPercentAsync(CancellationToken ct = default)
    {
        // True utilization = busy jiffies / total jiffies over a short window, read from the aggregate
        // "cpu" line of /proc/stat (already summed across every core ⇒ the per-core average). This is the
        // instantaneous "% busy" the dashboard wants — unlike /proc/loadavg's 1-minute run-queue average.
        try
        {
            var (idle1, total1) = ReadCpuJiffies();
            await Task.Delay(CpuSampleMs, ct);
            var (idle2, total2) = ReadCpuJiffies();
            return CpuBusyPercent(idle2 - idle1, total2 - total1);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return 0;
        }
    }

    // --- network ------------------------------------------------------------------------------
    public async Task<NetworkStatus> GetNetworkStatusAsync(CancellationToken ct = default)
    {
        // Find the active device: nmcli -t -f DEVICE,TYPE,STATE,CONNECTION device
        var dev = await ProcessRunner.RunAsync("nmcli", "-t -f DEVICE,TYPE,STATE,CONNECTION device", ct);
        string iface = "", type = "";
        foreach (var line in dev.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = line.Split(':');
            if (p.Length >= 3 && p[2] == "connected" && p[1] is "ethernet" or "wifi")
            {
                iface = p[0];
                type = p[1];
                break;
            }
        }
        if (iface.Length == 0)
            return new NetworkStatus(NetworkLink.None, "", "", null, null, false);

        var ip = (await NmcliFieldAsync(ct, "-g", "IP4.ADDRESS", "device", "show", iface)).Split('/')[0];
        var conn = await ActiveConnectionAsync(iface, ct);
        var method = await NmcliFieldAsync(ct, "-g", "ipv4.method", "connection", "show", conn);
        var isStatic = string.Equals(method, "manual", StringComparison.OrdinalIgnoreCase);

        if (type == "wifi")
        {
            var wifi = (await ProcessRunner.RunAsync("nmcli", "-t -f ACTIVE,SSID,SIGNAL dev wifi", ct)).StdOut;
            foreach (var line in wifi.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var p = line.Split(':');
                if (p.Length >= 3 && p[0] == "yes")
                    return new NetworkStatus(NetworkLink.WiFi, iface, ip, p[1], int.TryParse(p[2], out var s) ? s : null, isStatic);
            }
            return new NetworkStatus(NetworkLink.WiFi, iface, ip, null, null, isStatic);
        }
        return new NetworkStatus(NetworkLink.Cable, iface, ip, null, null, isStatic);
    }

    public async Task ApplyNetworkConfigAsync(OsNetworkConfig config, CancellationToken ct = default)
    {
        var iface = config.PreferredLink == NetworkLink.WiFi ? _o.WifiInterface : _o.EthernetInterface;
        var conn = await ActiveConnectionAsync(iface, ct);
        if (conn.Length == 0)
        {
            logger.LogWarning("ApplyNetworkConfig: nenhuma conexão ativa em {Iface}.", iface);
            return;
        }

        if (config.StaticIp)
        {
            var prefix = MaskToPrefix(config.Mask);
            // nmcli takes the DNS list as a single space-separated value (one argv token).
            var dns = string.Join(' ', new[] { config.DnsPrimary, config.DnsSecondary }.Where(d => !string.IsNullOrWhiteSpace(d)));
            await NmcliAsync(ct, "connection", "modify", conn,
                "ipv4.method", "manual",
                "ipv4.addresses", $"{config.Ip}/{prefix}",
                "ipv4.gateway", config.Gateway,
                "ipv4.dns", dns);
        }
        else
        {
            await NmcliAsync(ct, "connection", "modify", conn, "ipv4.method", "auto");
        }
        await NmcliAsync(ct, "connection", "up", conn);
        logger.LogInformation("Rede aplicada em {Conn}: {Mode}.", conn, config.StaticIp ? "IP fixo" : "DHCP");
    }

    public async Task<IReadOnlyList<WifiNetwork>> ScanWifiAsync(CancellationToken ct = default)
    {
        var res = await ProcessRunner.RunAsync("nmcli", "-t -f ACTIVE,SSID,SIGNAL,SECURITY dev wifi", ct);
        var list = new List<WifiNetwork>();
        foreach (var line in res.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = line.Split(':');
            if (p.Length >= 4 && p[1].Length > 0)
                list.Add(new WifiNetwork(p[1], int.TryParse(p[2], out var s) ? s : 0, p[3].Length > 0 && p[3] != "--", p[0] == "yes"));
        }
        return list;
    }

    public async Task ConnectWifiAsync(string ssid, string? password, CancellationToken ct = default)
    {
        string[] args = string.IsNullOrEmpty(password)
            ? ["device", "wifi", "connect", ssid]
            : ["device", "wifi", "connect", ssid, "password", password];
        await NmcliAsync(ct, args);
        logger.LogInformation("Wi-Fi conectado: '{Ssid}'.", ssid);
    }

    public async Task<IReadOnlyList<NetworkInterfaceInfo>> ListInterfacesAsync(CancellationToken ct = default)
    {
        var res = await ProcessRunner.RunAsync("nmcli", "-t -f DEVICE,TYPE,STATE device", ct);
        var list = new List<NetworkInterfaceInfo>();
        foreach (var line in res.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = line.Split(':');
            if (p.Length < 3) continue;
            InterfaceKind? kind = p[1] switch { "ethernet" => InterfaceKind.Ethernet, "wifi" => InterfaceKind.WiFi, _ => null };
            if (kind is null) continue;
            var up = p[2] == "connected";
            var ip = up ? (await NmcliFieldAsync(ct, "-g", "IP4.ADDRESS", "device", "show", p[0])).Split('/')[0] : "";
            list.Add(new NetworkInterfaceInfo(p[0], kind.Value, up, ip));
        }
        return list;
    }

    public async Task SetPriorityInterfaceAsync(string interfaceName, CancellationToken ct = default)
    {
        var conn = await ActiveConnectionAsync(interfaceName, ct);
        if (conn.Length == 0)
        {
            logger.LogWarning("SetPriorityInterface: nenhuma conexão ativa em {Iface}.", interfaceName);
            return;
        }
        // Higher autoconnect-priority wins; bump this connection and bring it up so it becomes preferred.
        await NmcliAsync(ct, "connection", "modify", conn, "connection.autoconnect-priority", "100");
        await NmcliAsync(ct, "connection", "up", conn);
        logger.LogInformation("Interface prioritária: {Iface} ({Conn}).", interfaceName, conn);
    }

    // --- time / NTP ---------------------------------------------------------------------------
    public async Task<TimeStatus> GetTimeStatusAsync(CancellationToken ct = default)
    {
        var res = await ProcessRunner.RunAsync("timedatectl", "show -p Timezone -p NTP -p NTPSynchronized", ct);
        var map = res.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Split('=', 2)).Where(p => p.Length == 2)
            .ToDictionary(p => p[0], p => p[1]);
        return new TimeStatus(
            clock.UtcNow,
            map.GetValueOrDefault("Timezone", "UTC"),
            map.GetValueOrDefault("NTPSynchronized") == "yes",
            map.GetValueOrDefault("NTP") == "yes");
    }

    public async Task SetTimeAsync(DateTimeOffset time, CancellationToken ct = default) =>
        await RunPrivileged("timedatectl", ct, "set-time", time.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss"));

    public async Task SetNtpAsync(bool enabled, CancellationToken ct = default) =>
        await RunPrivileged("timedatectl", ct, "set-ntp", enabled ? "true" : "false");

    // --- software update ----------------------------------------------------------------------
    public async Task<UpdateStatus> GetUpdateStatusAsync(CancellationToken ct = default)
    {
        var current = _o.CurrentVersion;
        if (string.IsNullOrWhiteSpace(_o.UpdateCheckUrl))
            return new UpdateStatus(current, null, false);
        try
        {
            using var http = httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(5);
            var available = (await http.GetStringAsync(_o.UpdateCheckUrl, ct)).Trim();
            return new UpdateStatus(current, available, available.Length > 0 && available != current);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao consultar atualização em {Url}.", _o.UpdateCheckUrl);
            return new UpdateStatus(current, null, false);
        }
    }

    public async Task ApplyUpdateAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_o.UpdateCommand))
        {
            logger.LogWarning("ApplyUpdate: System:UpdateCommand não configurado.");
            return;
        }
        logger.LogWarning("Aplicando atualização de software: {Cmd}", _o.UpdateCommand);
        // Pass the operator-configured command as one verbatim token to `sh -c` (allows pipes/&&) so the
        // .NET argument tokenizer can't mangle its quotes/whitespace.
        await ProcessRunner.RunAsync("/bin/sh", ["-c", _o.UpdateCommand], ct);
    }

    // --- connectivity / power -----------------------------------------------------------------
    public async Task<bool> PingCentralServerAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_o.CentralServerUrl)) return false;
        try
        {
            using var http = httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(4);
            var resp = await http.GetAsync(_o.CentralServerUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public Task RebootAsync(CancellationToken ct = default) => RunPrivileged("systemctl", ct, "reboot");
    public Task ShutdownAsync(CancellationToken ct = default) => RunPrivileged("systemctl", ct, "poweroff");

    // --- helpers ------------------------------------------------------------------------------
    private async Task NmcliAsync(CancellationToken ct, params string[] args)
    {
        var r = await ProcessRunner.RunAsync("nmcli", args, ct);
        if (!r.Ok) logger.LogWarning("nmcli {Args} falhou ({Code}): {Err}", string.Join(' ', args), r.ExitCode, r.StdErr);
    }

    private async Task<string> NmcliFieldAsync(CancellationToken ct, params string[] args) =>
        (await ProcessRunner.RunAsync("nmcli", args, ct)).StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";

    private async Task<string> ActiveConnectionAsync(string iface, CancellationToken ct)
    {
        var res = await ProcessRunner.RunAsync("nmcli", "-t -f DEVICE,CONNECTION device", ct);
        foreach (var line in res.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = line.Split(':');
            if (p.Length >= 2 && p[0] == iface) return p[1];
        }
        return "";
    }

    private async Task RunPrivileged(string file, CancellationToken ct, params string[] args)
    {
        var r = await ProcessRunner.RunAsync(file, args, ct);
        if (!r.Ok) logger.LogError("{File} {Args} falhou ({Code}): {Err}", file, string.Join(' ', args), r.ExitCode, r.StdErr);
    }

    private static int MaskToPrefix(string mask)
    {
        // "255.255.255.0" -> 24; falls back to /24 on a malformed mask.
        try
        {
            return mask.Split('.').Select(byte.Parse)
                .Aggregate(0, (acc, b) => acc + System.Numerics.BitOperations.PopCount((uint)b));
        }
        catch { return 24; }
    }

    private static string ReadOsPrettyName()
    {
        try
        {
            foreach (var line in File.ReadLines("/etc/os-release"))
                if (line.StartsWith("PRETTY_NAME=", StringComparison.Ordinal))
                    return line["PRETTY_NAME=".Length..].Trim('"');
        }
        catch { /* ignore */ }
        return Environment.OSVersion.ToString();
    }

    private static long ReadUptimeSeconds()
    {
        try
        {
            var first = File.ReadAllText("/proc/uptime").Split(' ')[0];
            return (long)double.Parse(first, CultureInfo.InvariantCulture);
        }
        catch { return 0; }
    }

    /// <summary>CPU "% busy" from two /proc/stat snapshots: <c>(total − idle) / total</c> over the window,
    /// clamped to 0–100 and rounded. Pure (no I/O) so it is unit-testable; returns 0 for a non-positive
    /// window.</summary>
    public static double CpuBusyPercent(long idleDelta, long totalDelta) =>
        totalDelta <= 0 ? 0 : Math.Round(Math.Clamp(100.0 * (totalDelta - idleDelta) / totalDelta, 0, 100), 1);

    // Aggregate "cpu" line: "cpu user nice system idle iowait irq softirq steal guest guest_nice".
    // idle-time = idle + iowait; total = sum of every numeric column (summed over all cores by the kernel).
    private static (long idle, long total) ReadCpuJiffies()
    {
        var line = File.ReadLines("/proc/stat").First(l => l.StartsWith("cpu ", StringComparison.Ordinal));
        var cols = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        long total = 0;
        for (var i = 1; i < cols.Length; i++) total += long.Parse(cols[i], CultureInfo.InvariantCulture);
        var idle = long.Parse(cols[4], CultureInfo.InvariantCulture);
        if (cols.Length > 5) idle += long.Parse(cols[5], CultureInfo.InvariantCulture); // + iowait
        return (idle, total);
    }

    private static double? ReadCpuTempC()
    {
        try
        {
            var milli = int.Parse(File.ReadAllText("/sys/class/thermal/thermal_zone0/temp").Trim());
            return Math.Round(milli / 1000.0, 1);
        }
        catch { return null; }
    }

    private static (long usedMB, long totalMB) ReadMemoryMB()
    {
        try
        {
            long total = 0, available = 0;
            foreach (var line in File.ReadLines("/proc/meminfo"))
            {
                if (line.StartsWith("MemTotal:", StringComparison.Ordinal)) total = ParseKb(line);
                else if (line.StartsWith("MemAvailable:", StringComparison.Ordinal)) available = ParseKb(line);
                if (total > 0 && available > 0) break;
            }
            return ((total - available) / 1024, total / 1024);

            static long ParseKb(string line) => long.Parse(line.Split([' '], StringSplitOptions.RemoveEmptyEntries)[1]);
        }
        catch { return (0, 0); }
    }
}
