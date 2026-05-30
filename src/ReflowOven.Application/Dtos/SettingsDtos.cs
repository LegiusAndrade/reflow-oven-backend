namespace ReflowOven.Application.Dtos;

public sealed record PidDto(double P, double I, double D);

public sealed record OvenDto(int MaxTemp, int MaxFanRpm);

public sealed record ProcessDto(int MaxExtraTimeSec);

public sealed record VoltageDto(int Min, int Max);

public sealed record NetworkDto(
    string Ip,
    string Mask,
    string Gateway,
    string DnsPrimary,
    string DnsSecondary,
    bool StaticIp);

public sealed record NotificationSettingDto(
    string Id,
    string Alert,
    NotificationProcess Process,
    bool Buzzer,
    BuzzerSound Sound,
    NotificationKind Kind);

/// <summary>Default visibility of each execution-chart series (camelCase keys match the frontend Record).</summary>
public sealed record RunSeriesDto(
    bool Alvo,
    bool Oven,
    bool Board,
    bool Current,
    bool Voltage,
    bool OvenFan,
    bool BoardFan);

public sealed record RunConfigDto(RunSeriesDto Series);

/// <summary>The full Configurações payload (used for both GET and PUT).</summary>
public sealed record SettingsDto(
    PidDto Pid,
    OvenDto Oven,
    ProcessDto Process,
    VoltageDto Voltage,
    NetworkDto Network,
    IReadOnlyList<NotificationSettingDto> Notifications,
    RunConfigDto Run);
