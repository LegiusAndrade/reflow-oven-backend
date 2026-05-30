namespace ReflowOven.Application.Dtos;

public sealed record OsInfoDto(string Name, string Kernel);

public sealed record BoardDto(BoardRole Role, string Version, string Serial, int Hours);

/// <summary>The Informação screen payload: system card + one card per board.</summary>
public sealed record DeviceInfoDto(
    double StorageFreeGB,
    double StorageTotalGB,
    string FirmwareVersion,
    string HtmlVersion,
    string BackendVersion,
    string BoardIp,
    OsInfoDto Os,
    IReadOnlyList<BoardDto> Boards);
