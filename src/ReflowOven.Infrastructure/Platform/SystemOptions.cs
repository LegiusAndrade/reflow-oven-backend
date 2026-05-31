namespace ReflowOven.Infrastructure.Platform;

/// <summary>System/OS config (<c>System</c> section). <c>Mode=Simulated</c> (dev) or <c>Linux</c> (OrangePi).</summary>
public sealed class SystemOptions
{
    public const string Section = "System";

    /// <summary><c>Simulated</c> (default — fake data, no OS calls) or <c>Linux</c> (real nmcli/systemctl).</summary>
    public string Mode { get; set; } = "Simulated";

    /// <summary>Installed software version (shown on the Informação screen and compared for updates).</summary>
    public string CurrentVersion { get; set; } = "1.3.0";

    /// <summary>HTTP(S) endpoint polled to know if the device can reach the central server (the globe icon).</summary>
    public string? CentralServerUrl { get; set; }

    /// <summary>HTTP(S) endpoint returning the latest available version as plain text (OTA check). Optional.</summary>
    public string? UpdateCheckUrl { get; set; }

    /// <summary>Shell command the Linux controller runs to apply an update (e.g. a self-update script).</summary>
    public string UpdateCommand { get; set; } = "";

    /// <summary>Wired interface name on the Pi.</summary>
    public string EthernetInterface { get; set; } = "eth0";

    /// <summary>Wireless interface name on the Pi.</summary>
    public string WifiInterface { get; set; } = "wlan0";

    /// <summary>How often the background monitor polls central-server reachability + OTA status (seconds, min 10).</summary>
    public int MonitorIntervalSeconds { get; set; } = 60;
}
