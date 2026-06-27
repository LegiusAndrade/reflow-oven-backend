namespace ReflowOven.Application.Dtos;

// --- shared timeline / curve rows -------------------------------------------------------
public sealed record LogEventDto(DateTimeOffset At, LogEventKind Kind, string Message);

public sealed record ExecProfilePointDto(int T, int Temp, ProfileRole Kind);

public sealed record ProfileComparisonRowDto(
    int TempProg,
    int TempReal,
    int TimeProgSeconds,
    int TimeRealSeconds,
    int StageIndex);

// --- executions -------------------------------------------------------------------------
public sealed record ExecutionSummaryDto(
    Guid Id,
    string? ProgramId,
    string ProgramName,
    string? UserName,
    DateTimeOffset StartedAt,
    int DurationSeconds,
    ExecutionStatus Status,
    int PeakTemp,
    decimal PeakCurrent);

public sealed record ExecutionDetailDto(
    Guid Id,
    string? ProgramId,
    string ProgramName,
    Guid? UserId,
    string? UserName,
    DateTimeOffset StartedAt,
    int DurationSeconds,
    ExecutionStatus Status,
    int PeakTemp,
    decimal PeakCurrent,
    int? FaultAtT,
    int? FaultAtTemp,
    IReadOnlyList<ExecProfilePointDto> Points,
    IReadOnlyList<ProfileComparisonRowDto> Comparison,
    IReadOnlyList<LogEventDto> Events,
    FailureSnapshotDto Trace,
    string? FailureReason = null,
    string? ErrorCode = null,
    Guid? LinkedErrorId = null);

// --- errors -----------------------------------------------------------------------------
public sealed record SnapshotSeriesDto(string Name, string Unit, string Color, double[] Values);

public sealed record FailureSnapshotDto(int DurationSec, IReadOnlyList<SnapshotSeriesDto> Series);

/// <summary>One decoded sample of the board fault-snapshot buffer (GET_FAULT_SNAPSHOT) in engineering units.</summary>
public sealed record BoardFaultSampleDto(
    double OvenTempC,
    double BoardTempC,
    double VbusV,
    double VregV,
    double PdV,
    double CurrentA,
    int FanIntakeRpm,
    int FanExhaustRpm,
    int FanBoardRpm,
    int DutyIntakePct,
    int DutyExhaustPct,
    int DutyBoardPct,
    double McuTempC,
    double VddaV,
    int FaultFlags,
    double SetpointC,
    int BuckDutyPct,
    int PowerW);

/// <summary>The board's raw fault-snapshot telemetry buffer attached to a fault — metadata + the per-10 ms
/// samples in engineering units. Null on a fault recorded without a successful board download.</summary>
public sealed record BoardFaultSnapshotDto(
    int SampleIntervalMs,
    int TriggerIndex,
    int FaultCode,
    IReadOnlyList<BoardFaultSampleDto> Samples);

public sealed record ErrorSummaryDto(
    Guid Id,
    DateTimeOffset At,
    string FaultTypeCode,
    ErrorSeverity Severity,
    string Message,
    string? UserName,
    string? ProgramName);

public sealed record ErrorDetailDto(
    Guid Id,
    DateTimeOffset At,
    string FaultTypeCode,
    ErrorSeverity Severity,
    string Message,
    Guid? UserId,
    string? UserName,
    string? ProgramId,
    string? ProgramName,
    int OvenTemp,
    int PcbTemp,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    int InputVoltage,
    int OutputVoltage,
    FailureSnapshotDto Snapshot,
    IReadOnlyList<LogEventDto> Events,
    /// <summary>The raw board fault-snapshot buffer (GET_FAULT_SNAPSHOT) downloaded around this fault, or null
    /// when the board offered none / the download failed. Distinct from <see cref="Snapshot"/> (the presentational
    /// chart series); omitted from the JSON when null.</summary>
    BoardFaultSnapshotDto? BoardSnapshot = null);

// --- changes ----------------------------------------------------------------------------
public sealed record ChangePointRowDto(int Index, int Temp, int TimeSec, RampShape Ramp, ChangePointRole Role);

/// <summary>One side of a per-point diff (before or after edit). Null when the point exists on only one side.</summary>
public sealed record ChangePointValueDto(double Temp, int TimeSec, RampShape Ramp);

/// <summary>
/// Consolidated per-point diff row: exactly one row per index. <c>Status</c> is
/// <c>unchanged|changed|added|removed</c>; <c>ChangedFields</c> lists which of
/// <c>temp|timeSec|ramp</c> differ (only for status=changed).
/// </summary>
public sealed record ChangePointDiffDto(
    int Index,
    string Status,
    ChangePointValueDto? Before,
    ChangePointValueDto? After,
    IReadOnlyList<string> ChangedFields);

public sealed record ChangeDiffSummaryDto(
    int TotalChanges,
    int Added,
    int Removed,
    int Changed,
    int Unchanged,
    IReadOnlyDictionary<string, int> ChangedFields);

public sealed record ChangeDiffDto(
    ChangeDiffSummaryDto Summary,
    IReadOnlyList<ChangePointDiffDto> Points);

public sealed record ChangeSummaryDto(
    Guid Id,
    DateTimeOffset At,
    ChangeAction Action,
    string Target,
    string? UserName,
    ChangeDetailKind DetailKind);

public sealed record ChangeDetailDto(
    Guid Id,
    DateTimeOffset At,
    ChangeAction Action,
    string Target,
    string? UserName,
    string? ProgramId,
    ChangeDetailKind DetailKind,
    IReadOnlyList<string>? ConfigBullets,
    IReadOnlyList<ChangePointRowDto> Points,
    ChangeDiffDto? Diff = null,
    IReadOnlyList<ProfilePointDto>? BeforeCurve = null,
    IReadOnlyList<ProfilePointDto>? AfterCurve = null);

// --- fault catalog & system log ---------------------------------------------------------
public sealed record FaultTypeDto(string Code, ErrorSeverity Severity, string Message);

public sealed record SystemLogDto(long Id, DateTimeOffset At, LogLevel Level, string Message);

/// <summary>
/// Common report list filters (search + date range, paged) plus an optional per-tab category filter,
/// applied server-side so server-paginated lists filter the whole set, not just the current page. Each
/// category field carries the pt-BR wire literal of its enum (e.g. <c>status=Concluído</c>) and is
/// ignored if unrecognized. <c>To</c> is treated as the end of that day (inclusive).
/// </summary>
public sealed record ReportQuery(
    string? Search = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    int Page = 1,
    int PageSize = 10,
    string? Status = null,
    string? Action = null,
    string? Severity = null,
    string? Level = null,
    string? ProgramId = null,
    /// <summary>Strictly-earlier cursor (exclusive): keep rows with <c>At &lt; Before</c>. Distinct
    /// from <c>To</c> (inclusive end-of-day).</summary>
    DateTimeOffset? Before = null,
    /// <summary>Operation-log filters (Log de Operação): <c>Operator</c> name (contains, case-insensitive),
    /// <c>Type</c>/<c>Object</c> as the enum wire literals (ignored if unrecognized) and an exact
    /// <c>ObjectId</c>. Ignored by the other report lists.</summary>
    string? Operator = null,
    string? Type = null,
    string? Object = null,
    string? ObjectId = null,
    string? Category = null);
