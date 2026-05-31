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
    FailureSnapshotDto Trace);

// --- errors -----------------------------------------------------------------------------
public sealed record SnapshotSeriesDto(string Name, string Unit, string Color, double[] Values);

public sealed record FailureSnapshotDto(int DurationSec, IReadOnlyList<SnapshotSeriesDto> Series);

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
    IReadOnlyList<LogEventDto> Events);

// --- changes ----------------------------------------------------------------------------
public sealed record ChangePointRowDto(int Index, int Temp, int TimeSec, RampShape Ramp, ChangePointRole Role);

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
    IReadOnlyList<ChangePointRowDto> Points);

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
    string? ProgramId = null);
