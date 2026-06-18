namespace ReflowOven.Domain.Entities;

/// <summary>
/// The durable record of one PID relay auto-tune (the Autotune history screen). Captures when it ran, who
/// triggered it, the outcome (identified Ku/Tu + derived Kp/Ki/Kd) and — on failure — the inferred reason and
/// any board fault present, since the firmware's 0x0D reply carries only a generic FAILED state with no code.
/// The gains it produces are NOT applied automatically: the operator confirms (<see cref="Applied"/>) or
/// discards (<see cref="Dismissed"/>) them on the screen.
/// </summary>
public class AutotuneRun
{
    public Guid Id { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    /// <summary>When it reached a terminal state (done/failed/cancelled); null while still running.</summary>
    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>Wall-clock length in seconds (set at finalize).</summary>
    public int DurationSeconds { get; set; }

    public AutotuneStatus Status { get; set; }

    /// <summary>Oscillation setpoint (°C) the operator chose for the relay experiment.</summary>
    public double TargetTempC { get; set; }

    /// <summary>Valid relay cycles averaged — progress while running, final once done.</summary>
    public int Cycles { get; set; }

    // --- Result (null until the tune reaches Concluído) -------------------------------------
    /// <summary>Ultimate gain Ku (V/°C).</summary>
    public double? Ku { get; set; }

    /// <summary>Ultimate period Tu (milliseconds).</summary>
    public int? TuMs { get; set; }

    public double? Kp { get; set; }
    public double? Ki { get; set; }
    public double? Kd { get; set; }

    // --- Gains in effect when the tune STARTED (so the screen can show before→after) --------
    public double PrevKp { get; set; }
    public double PrevKi { get; set; }
    public double PrevKd { get; set; }

    /// <summary>True once the operator applied the tuned gains to Settings (and they were pushed to the board).</summary>
    public bool Applied { get; set; }

    /// <summary>True once the operator dismissed the "aplicar?" prompt without applying (clears the banner).</summary>
    public bool Dismissed { get; set; }

    // --- Failure / cancel detail ------------------------------------------------------------
    /// <summary>Inferred cause (the 0x0D reply has no error code): operator cancel, a board fault, comms loss,
    /// or no convergence (timeout / no usable oscillation).</summary>
    public string? ErrorReason { get; set; }

    /// <summary>The catalogued board fault (E-1xx) present when the tune failed, if any.</summary>
    public string? FaultCode { get; set; }

    public Guid? UserId { get; set; }
    public string? UserName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
