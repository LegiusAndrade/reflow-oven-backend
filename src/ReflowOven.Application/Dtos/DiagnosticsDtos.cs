namespace ReflowOven.Application.Dtos;

/// <summary>Headline counts on the Diagnóstico → Estatísticas overview.</summary>
public sealed record DiagnosticsStatsDto(
    int Programs,
    int Executions,
    int Failures,
    int ActiveUsers,
    int InactiveUsers,
    int Admins);

/// <summary>Failure count grouped by fault type (bars by severity).</summary>
public sealed record FaultStatDto(string Code, ErrorSeverity Severity, string Message, int Count);

public sealed record RankedUserDto(string Id, string Name, long Logins);

public sealed record RankedProgramDto(string Id, string Name, int RunCount);

public sealed record DiagnosticsOverviewDto(
    DiagnosticsStatsDto Stats,
    IReadOnlyList<FaultStatDto> FaultsByType,
    IReadOnlyList<RankedUserDto> TopUsers,
    IReadOnlyList<RankedProgramDto> TopPrograms);

/// <summary>Live sensor readings (Diagnóstico → Sensores), also pushed over the diagnostics hub.</summary>
public sealed record SensorReadingsDto(
    double BoardTempC,
    double BoardFanRpm,
    double OvenTempC,
    double OvenFanRpm,
    double VoltageV,
    double CurrentA)
{
    public static SensorReadingsDto From(SensorReadings r) =>
        new(r.BoardTempC, r.BoardFanRpm, r.OvenTempC, r.OvenFanRpm, r.VoltageV, r.CurrentA);
}

public sealed record SelfTestResultDto(SelfTestId Id, SelfTestState State, string? Message)
{
    public static SelfTestResultDto From(SelfTestResult r) => new(r.Id, r.State, r.Message);
}

public sealed record SelfTestRequest(SelfTestId Id);

public sealed record PingRequest(string Host);

public sealed record PingResultDto(bool Ok, double Ms, string Host);
