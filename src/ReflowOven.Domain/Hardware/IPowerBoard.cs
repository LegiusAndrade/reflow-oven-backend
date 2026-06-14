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

    /// <summary>Stop/abort the current run (heater off, fans to a safe state).</summary>
    Task StopAsync(CancellationToken ct = default);

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
}
