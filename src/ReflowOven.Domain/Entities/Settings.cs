namespace ReflowOven.Domain.Entities;

/// <summary>
/// Device settings singleton (Id is always 1; enforced by a single-row check constraint). Mirrors
/// the nested shape of the frontend <c>Settings</c> type so the JSON contract round-trips.
/// </summary>
public class Settings
{
    public int Id { get; set; } = 1;

    public PidGains Pid { get; set; } = new();
    public OvenLimits Oven { get; set; } = new();
    public ProcessConfig Process { get; set; } = new();
    public VoltageThresholds Voltage { get; set; } = new();
    public NetworkConfig Network { get; set; } = new();

    /// <summary>11 fixed, edited-in-place alert rows.</summary>
    public List<NotificationSetting> Notifications { get; set; } = new();

    /// <summary>7 fixed default-visibility flags for the execution chart series.</summary>
    public List<RunSeriesPreference> RunSeries { get; set; } = new();
}

public class PidGains
{
    public double P { get; set; }
    public double I { get; set; }
    public double D { get; set; }
}

public class OvenLimits
{
    public int MaxTemp { get; set; }
    public int MaxFanRpm { get; set; }
}

public class ProcessConfig
{
    public int MaxExtraTimeSec { get; set; }
}

public class VoltageThresholds
{
    public int Min { get; set; }
    public int Max { get; set; }
}

public class NetworkConfig
{
    public string Ip { get; set; } = "";
    public string Mask { get; set; } = "";
    public string Gateway { get; set; } = "";
    public string DnsPrimary { get; set; } = "";
    public string DnsSecondary { get; set; } = "";
    public bool StaticIp { get; set; }
}

/// <summary>One configurable alert in the Notificações tab. Natural key = <see cref="Id"/> slug.</summary>
public class NotificationSetting
{
    public string Id { get; set; } = "";
    public int SettingsId { get; set; } = 1;

    /// <summary>Display order (the rows are fixed and edited in place).</summary>
    public int Order { get; set; }

    public string Alert { get; set; } = "";
    public NotificationProcess Process { get; set; }
    public bool Buzzer { get; set; }
    public BuzzerSound Sound { get; set; }
    public NotificationKind Kind { get; set; }
}

/// <summary>Default-visibility flag for one execution-chart signal. Key = (SettingsId, Signal).</summary>
public class RunSeriesPreference
{
    public int SettingsId { get; set; } = 1;
    public RunSignalId Signal { get; set; }
    public bool Visible { get; set; }
}
