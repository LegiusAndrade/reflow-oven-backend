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

    /// <summary>Input mains voltage (VAC) at the fault, or null when not measured. The power board has no
    /// mains-voltage channel today, so runtime faults persist null — never a fabricated reading (the UI
    /// renders "—"). Only demo-seeded rows carry a synthetic plausible value.</summary>
    public int? InputVoltage { get; set; }

    /// <summary>Output voltage (VDC, 0..180).</summary>
    public int OutputVoltage { get; set; }

    /// <summary>Multi-channel snapshot around the fault. Owned/jsonb.</summary>
    public FailureSnapshot Snapshot { get; set; } = new();

    /// <summary>The power board's raw fault-snapshot telemetry buffer (GET_FAULT_SNAPSHOT, 0x10), downloaded
    /// around the trigger and stored verbatim in engineering units. Null when the board offered none or the
    /// download failed (best-effort). Owned/jsonb.</summary>
    public BoardFaultSnapshot? BoardSnapshot { get; set; }

    public List<LogEvent> Events { get; set; } = new();
}

/// <summary>
/// The telemetry ring the power board captured around a protection fault, downloaded over RS422
/// (GET_FAULT_SNAPSHOT, 0x10) and persisted verbatim as jsonb. Distinct from the presentational
/// <see cref="FailureSnapshot"/> (the fixed-width chart series): this is the raw per-10 ms board buffer.
/// </summary>
public class BoardFaultSnapshot
{
    /// <summary>Spacing between samples in milliseconds (10 ms on the board).</summary>
    public int SampleIntervalMs { get; set; } = 10;

    /// <summary>Index into <see cref="Samples"/> at which the fault tripped.</summary>
    public int TriggerIndex { get; set; }

    /// <summary>The firmware fault code (u16) the board latched.</summary>
    public int FaultCode { get; set; }

    public List<BoardFaultSample> Samples { get; set; } = new();
}

/// <summary>One sample of a <see cref="BoardFaultSnapshot"/> in engineering units (°C, V, A, RPM, %, W). Owned/jsonb.</summary>
public class BoardFaultSample
{
    public double OvenTempC { get; set; }
    public double BoardTempC { get; set; }
    public double VbusV { get; set; }
    public double VregV { get; set; }
    public double PdV { get; set; }
    public double CurrentA { get; set; }
    public int FanIntakeRpm { get; set; }
    public int FanExhaustRpm { get; set; }
    public int FanBoardRpm { get; set; }
    public int DutyIntakePct { get; set; }
    public int DutyExhaustPct { get; set; }
    public int DutyBoardPct { get; set; }
    public double McuTempC { get; set; }
    public double VddaV { get; set; }
    public int FaultFlags { get; set; }
    public double SetpointC { get; set; }
    public int BuckDutyPct { get; set; }
    public int PowerW { get; set; }
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
