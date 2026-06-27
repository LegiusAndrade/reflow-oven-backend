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

/// <summary>The board's latched protection fault as carried on the diagnostics tick — null when the board is
/// OK. Derived from the status frame's fault code, mapped through the seeded <see cref="FaultType"/> catalog
/// for the pt-BR <see cref="ErrorSeverity"/> (<c>Crítico</c>/<c>Alerta</c>/<c>Aviso</c>) and message. The
/// JSON shape <c>fault: { code, severity, message } | null</c> is the contract with the frontend.</summary>
public sealed record FaultInfoDto(string Code, ErrorSeverity Severity, string Message)
{
    /// <summary>Map a latched E-code (or null when OK) to the fault info the diagnostics tick exposes.</summary>
    public static FaultInfoDto? From(string? code)
    {
        if (code is null) return null;
        var (severity, message) = FaultCatalog.Resolve(code);
        return new FaultInfoDto(code, severity, message);
    }
}

/// <summary>Live sensor readings (Diagnóstico → Sensores), also pushed over the diagnostics hub. <see
/// cref="Fault"/> is the board's latched protection fault (null when OK).</summary>
public sealed record SensorReadingsDto(
    double BoardTempC,
    int BoardFanRpm,
    double OvenTempC,
    int OvenFanRpm,
    double VoltageV,
    double CurrentA,
    FaultInfoDto? Fault)
{
    public static SensorReadingsDto From(SensorReadings r) =>
        new(r.BoardTempC, r.BoardFanRpm, r.OvenTempC, r.OvenFanRpm, r.VoltageV, r.CurrentA, FaultInfoDto.From(r.FaultCode));
}

public sealed record SelfTestResultDto(SelfTestId Id, SelfTestState State, string? Message)
{
    public static SelfTestResultDto From(SelfTestResult r) => new(r.Id, r.State, r.Message);
}

public sealed record SelfTestRequest(SelfTestId Id);

/// <summary>Ping a host. With <c>Port</c> set, measures a TCP connect (handshake latency); without it, ICMP.</summary>
public sealed record PingRequest(string Host, int? Port = null);

public sealed record PingResultDto(bool Ok, double Ms, string Host);
