namespace ReflowOven.Infrastructure.Platform;

/// <summary>System/OS config (<c>System</c> section). <c>Mode=Simulated</c> (dev) or <c>Linux</c> (OrangePi).</summary>
public sealed class SystemOptions
{
    public const string Section = "System";

    /// <summary><c>Simulated</c> (default — fake data, no OS calls) or <c>Linux</c> (real nmcli/systemctl).</summary>
    public string Mode { get; set; } = "Simulated";

    /// <summary>Which <c>IBoardGpio</c> to use ("Simulated" | "Linux"); unset = follow <see cref="Mode"/>.
    /// Lets the real GPIO (LEDs/power-good) run while the OS controller stays simulated — e.g. driving the
    /// LEDs from inside the dev container where systemd/nmcli aren't available.</summary>
    public string? GpioMode { get; set; }

    /// <summary>Installed software version (shown on the Informação screen and compared for updates).</summary>
    public string CurrentVersion { get; set; } = "1.3.0";

    /// <summary>HTTP(S) endpoint polled to know if the device can reach the central server (the globe icon).</summary>
    public string? CentralServerUrl { get; set; }

    /// <summary>URL of the local front (Next.js) server, pinged for the STATUS-LED "front" health. Empty
    /// (default) disables the check so a dev box never false-alarms; on the device set it to the kiosk URL
    /// (e.g. <c>http://localhost:3000</c>).</summary>
    public string? FrontUrl { get; set; }

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

    // --- Hardware clock (ISL1208 RTC) ---------------------------------------------------------------
    /// <summary>Hardware-RTC device written on a manual time-set so the clock survives a power cycle. The
    /// kernel reads it back into the system clock at boot (needs the device-tree overlay, e.g.
    /// <c>dtoverlay=i2c-rtc,isl1208</c>).</summary>
    public string RtcDevice { get; set; } = "/dev/rtc0";

    // --- Control-board GPIO (BCM/logical pin numbers; used when Mode=Linux) -------------------------
    /// <summary>Comms-activity LED — toggled on each frame received from the power board.</summary>
    public int CommLedPin { get; set; } = 13;

    /// <summary>Status LED — lit while the service is running.</summary>
    public int StatusLedPin { get; set; } = 6;

    /// <summary>Power-good input (true = power good).</summary>
    public int PowerGoodPin { get; set; } = 19;
}
