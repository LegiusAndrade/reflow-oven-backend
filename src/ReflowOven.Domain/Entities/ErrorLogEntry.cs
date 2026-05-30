namespace ReflowOven.Domain.Entities;

/// <summary>Seeded fault catalog (E-101..E-160). Source of the Diagnóstico "falhas por tipo" counts.</summary>
public class FaultType
{
    /// <summary>Stable code, e.g. "E-101". Primary key.</summary>
    public string Code { get; set; } = "";
    public ErrorSeverity Severity { get; set; }
    public string Message { get; set; } = "";

    public ICollection<ErrorLogEntry> Errors { get; set; } = new List<ErrorLogEntry>();
}

/// <summary>
/// A recorded fault — the source for Relatórios → Erros and its detail overlay. Severity and
/// message are denormalized from <see cref="FaultType"/> so historical rows are stable even if
/// the catalog text changes.
/// </summary>
public class ErrorLogEntry
{
    public Guid Id { get; set; }
    public DateTimeOffset At { get; set; }

    public string FaultTypeCode { get; set; } = "";
    public FaultType? FaultType { get; set; }

    public ErrorSeverity Severity { get; set; }
    public string Message { get; set; } = "";

    public Guid? UserId { get; set; }
    public string? UserName { get; set; }
    public string? ProgramId { get; set; }
    public string? ProgramName { get; set; }

    public int OvenTemp { get; set; }
    public int PcbTemp { get; set; }
    public DateTimeOffset StartAt { get; set; }
    public DateTimeOffset EndAt { get; set; }

    /// <summary>Input mains voltage (VAC, ~127).</summary>
    public int InputVoltage { get; set; }

    /// <summary>Output voltage (VDC, 0..180).</summary>
    public int OutputVoltage { get; set; }

    /// <summary>Multi-channel snapshot around the fault. Owned/jsonb.</summary>
    public FailureSnapshot Snapshot { get; set; } = new();

    public List<LogEvent> Events { get; set; } = new();
}

/// <summary>The fixed-width multi-signal trace captured around a fault. Owned/jsonb.</summary>
public class FailureSnapshot
{
    public int DurationSec { get; set; } = 60;
    public List<SnapshotSeries> Series { get; set; } = new();
}

/// <summary>One channel of a failure snapshot (e.g. Temp. Grelha). Owned/jsonb.</summary>
public class SnapshotSeries
{
    public string Name { get; set; } = "";
    public string Unit { get; set; } = "";

    /// <summary>Presentational color carried verbatim from the chart palette.</summary>
    public string Color { get; set; } = "";

    /// <summary><see cref="Common.DomainConstants.SnapshotSamples"/> values, ordered by x position.</summary>
    public double[] Values { get; set; } = [];
}
