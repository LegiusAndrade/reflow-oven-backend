namespace ReflowOven.Domain.Entities;

/// <summary>
/// The durable artifact of a completed (or failed) run — the source for the Relatórios →
/// Execuções tab and the run-detail overlay. Program/user names are snapshotted alongside the
/// FKs so history survives renames/deletes.
/// </summary>
public class ExecutionReport
{
    public Guid Id { get; set; }

    public string? ProgramId { get; set; }
    public string ProgramName { get; set; } = "";

    public Guid? UserId { get; set; }
    public string? UserName { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    /// <summary>Raw seconds (formatted as "5m 12s" on the client).</summary>
    public int DurationSeconds { get; set; }

    public ExecutionStatus Status { get; set; }

    public int PeakTemp { get; set; }
    public decimal PeakCurrent { get; set; }

    /// <summary>Set only on failed runs: when (s) and at what temperature (°C) the fault hit.</summary>
    public int? FaultAtT { get; set; }
    public int? FaultAtTemp { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Programmed + measured curve points (Kind discriminates). Stored as jsonb.</summary>
    public List<ExecProfilePoint> Points { get; set; } = new();

    /// <summary>Per-stage programmed-vs-real comparison rows. Stored as jsonb.</summary>
    public List<ProfileComparisonRow> Comparison { get; set; } = new();

    /// <summary>Event timeline (shared table with ErrorLogEntry).</summary>
    public List<LogEvent> Events { get; set; } = new();
}

/// <summary>A point on a stored execution curve. Owned/jsonb (read whole, bounded to 600 measured).</summary>
public class ExecProfilePoint
{
    public int T { get; set; }
    public int Temp { get; set; }
    public ProfileRole Kind { get; set; }
}

/// <summary>One row of the programmed-vs-real comparison table. Owned/jsonb. Deviation is computed client-side.</summary>
public class ProfileComparisonRow
{
    public int TempProg { get; set; }
    public int TempReal { get; set; }
    public int TimeProgSeconds { get; set; }
    public int TimeRealSeconds { get; set; }
    public int StageIndex { get; set; }
}

/// <summary>
/// A timeline event shown under an execution or an error. Exactly one of the two FKs is set.
/// Stored raw (DateTimeOffset, not "HH:MM:SS").
/// </summary>
public class LogEvent
{
    public long Id { get; set; }
    public DateTimeOffset At { get; set; }
    public LogEventKind Kind { get; set; }
    public string Message { get; set; } = "";

    public Guid? ExecutionReportId { get; set; }
    public ExecutionReport? ExecutionReport { get; set; }

    public Guid? ErrorLogEntryId { get; set; }
    public ErrorLogEntry? ErrorLogEntry { get; set; }

    public int OrderIndex { get; set; }
}
