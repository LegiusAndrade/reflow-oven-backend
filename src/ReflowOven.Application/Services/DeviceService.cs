namespace ReflowOven.Application.Services;

/// <summary>Informação screen backend: system card + a card per board (live disk + persisted versions).</summary>
public sealed class DeviceService(IAppDbContext db)
{
    public async Task<DeviceInfoDto> GetAsync(CancellationToken ct = default)
    {
        var info = await db.DeviceInfo.FirstOrDefaultAsync(x => x.Id == 1, ct)
            ?? throw new NotFoundException("Informações do dispositivo não inicializadas.");
        var boards = await db.Boards.OrderBy(b => b.Role).ToListAsync(ct);

        var (freeGB, totalGB) = DiskSpace();
        return new DeviceInfoDto(
            freeGB, totalGB,
            info.FirmwareVersion, info.HtmlVersion, info.BackendVersion, info.BoardIp,
            new OsInfoDto(info.Os.Name, info.Os.Kernel),
            boards.Select(b => new BoardDto(b.Role, b.Version, b.Serial, b.Hours)).ToList());
    }

    private static (double freeGB, double totalGB) DiskSpace()
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
