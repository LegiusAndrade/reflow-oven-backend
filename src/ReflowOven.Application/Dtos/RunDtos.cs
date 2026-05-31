namespace ReflowOven.Application.Dtos;

public sealed record StartRunRequest(string ProgramId);

/// <summary>One live sample pushed over the RunTelemetry hub (alvo = server-derived setpoint).</summary>
public sealed record TraceSampleDto(
    double T,
    double Alvo,
    double Oven,
    double Board,
    double Current,
    double Voltage,
    int OvenFan,
    int BoardFan)
{
    public static TraceSampleDto From(TraceSample s) =>
        new(s.T, s.Alvo, s.Oven, s.Board, s.Current, s.Voltage, s.OvenFan, s.BoardFan);
}

/// <summary>Current run status (polled via REST or pushed on change over SignalR).</summary>
public sealed record RunStatusDto(
    Guid RunId,
    string ProgramId,
    string ProgramName,
    RunStatus Status,
    RunPhase Phase,
    DateTimeOffset StartedAt,
    double ElapsedSeconds,
    double TotalSeconds,
    double Progress,
    TraceSampleDto? Last);
