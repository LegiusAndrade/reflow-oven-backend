namespace ReflowOven.Domain.Hardware;

/// <summary>
/// Abstraction over the STM32 power board. The board is reached over RS422 in production
/// (<c>Rs422PowerBoard</c>) and faked in development (<c>SimulatedPowerBoard</c>); everything
/// above this interface — controllers, the run loop, the telemetry hub — is hardware-agnostic.
/// </summary>
public interface IPowerBoard
{
    /// <summary>Read one telemetry tick.</summary>
    Task<SensorReadings> ReadAsync(CancellationToken ct = default);

    /// <summary>Begin a run. <paramref name="segments"/> is the editable profile the power board drives — it ships
    /// them and computes the setpoint curve itself (exact parabolas). <paramref name="profile"/> is the pre-sampled
    /// curve the simulator interpolates for telemetry. Process/PID limits reach the board via
    /// <see cref="ApplyControlConfigAsync"/>, not here.</summary>
    Task StartProgramAsync(IReadOnlyList<ProfileSegment> segments, IReadOnlyList<ProfilePoint> profile, CancellationToken ct = default);

    /// <summary>The board's live run state (real setpoint/phase/progress from its controller), or null when no
    /// controller is driving — the run loop then falls back to its own interpolated setpoint. Polled ~1 Hz.</summary>
    Task<RunReadback?> GetRunStatusAsync(CancellationToken ct = default);

    /// <summary>Stop/abort the current run (heater off, fans to a safe state).</summary>
    Task StopAsync(CancellationToken ct = default);

    /// <summary>ACK_FAULT (0x0F): acknowledge the board's latched protection fault and ask it to release the
    /// FAULT latch. REQUEST with no payload; the board replies OK and clears the latch once no critical
    /// condition remains (acknowledging a still-active fault is safe — the board stays latched and applies the
    /// acknowledgement when the cause clears). The cleared state is observed on the next periodic status push.
    /// On the simulator this simply clears the simulated fault back to OK.</summary>
    Task AcknowledgeFaultAsync(CancellationToken ct = default);

    /// <summary>Push sensor calibration (offsets/gain/fan PWM) to the board.</summary>
    Task ApplyCalibrationAsync(Calibration calibration, CancellationToken ct = default);

    /// <summary>Push control configuration (PID gains, oven/voltage/process limits) to the board.</summary>
    Task ApplyControlConfigAsync(Settings settings, CancellationToken ct = default);

    /// <summary>Run a single self-test and report its result.</summary>
    Task<SelfTestResult> RunSelfTestAsync(SelfTestId id, CancellationToken ct = default);

    /// <summary>Drive the output to a target voltage and read it back (calibration wizard sweep).</summary>
    Task<OutputCalStep> DriveOutputAsync(double setVoltage, CancellationToken ct = default);

    /// <summary>Versions / serials / hour-meters for the Informação screen.</summary>
    Task<BoardIdentity> GetIdentityAsync(CancellationToken ct = default);

    /// <summary>Raised when the board reports a fault (over-temp, over-current, RS422 loss, …).</summary>
    event EventHandler<FaultRaised>? FaultRaised;

    /// <summary>True while the RS422 link is alive (a status frame arrived within the watchdog window). The
    /// control board's STATUS LED uses this; the power board's OWN faults (over-temp, over-current, …) show
    /// on its own LED and reach the operator via the UI — they are not mirrored on the control board.</summary>
    bool IsConnected { get; }

    /// <summary>AUTOTUNE (0x0D): start/cancel/poll the firmware relay PID auto-tune. <paramref name="targetC"/>
    /// is the oscillation setpoint (°C), required for <see cref="AutoTuneOp.Start"/> and ignored otherwise.
    /// Returns the board current tune state and, once done, the identified gains.</summary>
    Task<AutoTuneReadback> AutoTuneAsync(AutoTuneOp op, double? targetC = null, CancellationToken ct = default);

    /// <summary>Raised when the power board asks the host for its control configuration (GET_CONFIGURATION,
    /// 0x0E): it does this on boot and retries until answered, staying in a safe (non-heating) state until it
    /// gets one. The handler should push the current config via <see cref="ApplyControlConfigAsync"/>.</summary>
    event EventHandler? ConfigRequested;
}
