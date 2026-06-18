namespace ReflowOven.Application.Dtos;

/// <summary>Request to start a relay auto-tune: the oscillation setpoint in °C.</summary>
public sealed record StartAutotuneRequest(double TargetTemp);

/// <summary>One auto-tune history row. <c>TriggeredBy</c> is the operator name (null = técnico de calibração).</summary>
public sealed record AutotuneRunDto(
    Guid Id,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    int DurationSeconds,
    AutotuneStatus Status,
    double TargetTemp,
    int Cycles,
    double? Ku,
    int? TuMs,
    double? Kp,
    double? Ki,
    double? Kd,
    double PrevKp,
    double PrevKi,
    double PrevKd,
    bool Applied,
    bool Dismissed,
    string? ErrorReason,
    string? FaultCode,
    string? TriggeredBy)
{
    public static AutotuneRunDto From(AutotuneRun r) => new(
        r.Id, r.StartedAt, r.FinishedAt, r.DurationSeconds, r.Status, r.TargetTempC, r.Cycles,
        r.Ku, r.TuMs, r.Kp, r.Ki, r.Kd, r.PrevKp, r.PrevKi, r.PrevKd, r.Applied, r.Dismissed,
        r.ErrorReason, r.FaultCode, r.UserName);
}

/// <summary>Live status of the active auto-tune (poll ~1 Hz), or the last finished one when idle.
/// <c>Running</c> is true only while the experiment is in progress.</summary>
public sealed record AutotuneStatusDto(bool Running, AutotuneRunDto? Current);

/// <summary>A page of auto-tune history. <c>Total</c> is the full count — "quantas vezes foi feito".</summary>
public sealed record AutotuneHistoryDto(int Total, int Page, int PageSize, IReadOnlyList<AutotuneRunDto> Items);
