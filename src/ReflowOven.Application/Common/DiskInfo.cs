namespace ReflowOven.Application.Common;

/// <summary>Local disk space of the volume hosting the app — one source of truth for Device/Maintenance
/// (and one try/catch fallback) instead of the same block copied in both services.</summary>
public static class DiskInfo
{
    /// <summary>Free/total space in GB of the volume hosting the app, or (0, 0) if it can't be read.</summary>
    public static (double freeGB, double totalGB) SpaceGB()
    {
        try
        {
            var root = Path.GetPathRoot(AppContext.BaseDirectory);
            var drive = new DriveInfo(string.IsNullOrEmpty(root) ? "/" : root);
            return (Math.Round(drive.AvailableFreeSpace / 1e9, 1), Math.Round(drive.TotalSize / 1e9, 1));
        }
        catch
        {
            return (0, 0);
        }
    }
}
