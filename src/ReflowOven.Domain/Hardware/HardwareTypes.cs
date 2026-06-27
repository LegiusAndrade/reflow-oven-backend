namespace ReflowOven.Domain.Hardware;

/// <summary>
/// One telemetry tick from the power board (mirrors the frontend <c>SensorReadings</c>):
/// grill type-K thermocouple, NTC heatsink, Hall-sensor output current, output voltage and fans.
/// <see cref="FaultCode"/> carries the board's latched protection fault as a catalogued E-code (the status
/// frame's <c>state == FAULT</c> + <c>fault_code</c>), or null when the board is OK — surfaced to the
/// Diagnóstico tick so an operator sees an active fault live.
/// </summary>
public readonly record struct SensorReadings(
    double BoardTempC,
    int BoardFanRpm,
    double OvenTempC,
    int OvenFanRpm,
    double VoltageV,
    double CurrentA,
    string? FaultCode = null);

/// <summary>The power board's live run state (GET_RUN_STATUS) while its controller is driving the run. The
/// driver returns null when no controller is driving (the simulator, or firmware reporting running=0); the run
/// loop then falls back to its own interpolated setpoint/phase. <see cref="DutyPct"/> is the heater duty 0..100.</summary>
public readonly record struct RunReadback(
    bool Running,
    RunPhase Phase,
    double SetpointC,
    int ElapsedSec,
    int TotalSec,
    int DutyPct);

/// <summary>
/// A single point on the live execution chart pushed over SignalR. <c>Alvo</c> is the
/// server-interpolated setpoint; the other seven are measured channels.
/// </summary>
public readonly record struct TraceSample(
    double T,
    double Alvo,
    double Oven,
    double Board,
    double Current,
    double Voltage,
    int OvenFan,
    int BoardFan);

/// <summary>Process safety limits handed to the board for a run (from Settings).</summary>
public readonly record struct ProcessLimits(
    int MaxTemp,
    int MaxFanRpm,
    int VoltageMin,
    int VoltageMax,
    int MaxExtraTimeSec);

public readonly record struct SelfTestResult(SelfTestId Id, SelfTestState State, string? Message = null);

/// <summary>One step of the output-calibration sweep wizard (set vs. measured voltage).</summary>
public readonly record struct OutputCalStep(double SetVoltage, double MeasuredVoltage);

/// <summary>A fault raised by the board; becomes an ErrorLogEntry and interrupts any run.</summary>
public readonly record struct FaultRaised(string FaultTypeCode, DateTimeOffset At);

/// <summary>Versions / serials / hour-meters reported by the boards (for the Informação screen).</summary>
public readonly record struct BoardIdentity(
    string PowerVersion,
    string PowerSerial,
    int PowerHours,
    string ControlVersion,
    string ControlSerial,
    int ControlHours);

/// <summary>
/// One 32-byte sample of a board fault snapshot (GET_FAULT_SNAPSHOT, 0x10), decoded from the packed wire
/// layout into engineering units: temperatures in °C, the rails in V, current in A, fans in RPM, duties in %
/// and power in W. The board captures one sample every <see cref="FaultSnapshot.SampleIntervalMs"/> (10 ms)
/// around a protection-fault trigger. <see cref="FaultFlags"/> is the raw firmware fault bitfield at that sample.
/// </summary>
public readonly record struct FaultSnapshotSample(
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

/// <summary>
/// The telemetry ring the power board captured around its last protection fault, downloaded over RS422 in
/// chunks (GET_FAULT_SNAPSHOT, 0x10) and decoded to engineering units. <see cref="TriggerIndex"/> is the sample
/// index at which the fault tripped; consecutive samples are <see cref="SampleIntervalMs"/> (10 ms) apart;
/// <see cref="FaultCode"/> is the firmware fault code (u16) the board latched. Becomes the jsonb snapshot
/// attached to the run's <c>ErrorLogEntry</c>.
/// </summary>
public sealed record FaultSnapshot(
    int SampleIntervalMs,
    int TriggerIndex,
    int FaultCode,
    long BaseMs,
    IReadOnlyList<FaultSnapshotSample> Samples);

/// <summary>Op for the AUTOTUNE command (0x0D): start the relay tune, cancel it, or poll its state.</summary>
public enum AutoTuneOp { Start = 0, Cancel = 1, Query = 2 }

/// <summary>Raw state of the firmware relay auto-tuner (AUTOTUNE_RELAY_STATE_t on the wire).</summary>
public enum AutoTuneState { Idle = 0, Running = 1, Done = 2, Failed = 3 }

/// <summary>One reply to an AUTOTUNE command (0x0D, 22 B): engine state + cycle progress and, once
/// <see cref="AutoTuneState.Done"/>, the identified ultimate gain/period plus the derived PID gains.
/// Ku is in V/°C; Tu in milliseconds; the gains are in the controller units (volts).</summary>
public readonly record struct AutoTuneReadback(
    AutoTuneState State,
    int Cycles,
    double Ku,
    int TuMs,
    double Kp,
    double Ki,
    double Kd);
