namespace ReflowOven.Application.Services;

/// <summary>Informação screen backend: system card + a card per board (live disk + persisted versions).</summary>
public sealed class DeviceService(IAppDbContext db)
{
    public async Task<DeviceInfoDto> GetAsync(CancellationToken ct = default)
    {
        var info = await db.DeviceInfo.AsNoTracking().FirstOrDefaultAsync(x => x.Id == 1, ct)
            ?? throw new NotFoundException("Informações do dispositivo não inicializadas.");
        var boards = await db.Boards.AsNoTracking().OrderBy(b => b.Role).ToListAsync(ct);

        var (freeGB, totalGB) = DiskInfo.SpaceGB();
        return new DeviceInfoDto(
            freeGB, totalGB,
            info.FirmwareVersion, info.HtmlVersion, info.BackendVersion, info.BoardIp,
            new OsInfoDto(info.Os.Name, info.Os.Kernel),
            boards.Select(b => new BoardDto(b.Role, b.Version, b.Serial, b.Hours)).ToList());
    }

}
