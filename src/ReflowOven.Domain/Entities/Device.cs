namespace ReflowOven.Domain.Entities;

/// <summary>
/// System/device info singleton (Id always 1) for the Informação screen. Some fields are read
/// live from the host (storage, IP, OS), others persisted (versions). <see cref="Os"/> is owned.
/// </summary>
public class DeviceInfo
{
    public int Id { get; set; } = 1;

    public double StorageFreeGB { get; set; }
    public double StorageTotalGB { get; set; }

    public string FirmwareVersion { get; set; } = "";
    public string HtmlVersion { get; set; } = "";
    public string BackendVersion { get; set; } = "";
    public string BoardIp { get; set; } = "";

    public OsInfo Os { get; set; } = new();
}

public class OsInfo
{
    public string Name { get; set; } = "";
    public string Kernel { get; set; } = "";
}

/// <summary>One physical board (Power or Control). Keyed by <see cref="Role"/>. Hours = lifetime hour-meter.</summary>
public class Board
{
    public BoardRole Role { get; set; }
    public string Version { get; set; } = "";
    public string Serial { get; set; } = "";
    public int Hours { get; set; }
}
