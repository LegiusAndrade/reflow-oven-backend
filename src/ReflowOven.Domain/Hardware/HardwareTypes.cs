namespace ReflowOven.Domain.Hardware;

/// <summary>
/// One telemetry tick from the power board (mirrors the frontend <c>SensorReadings</c>):
/// grill type-K thermocouple, NTC heatsink, Hall-sensor output current, output voltage and fans.
/// </summary>
public readonly record struct SensorReadings(
    double BoardTempC,
    int BoardFanRpm,
    double OvenTempC,
    int OvenFanRpm,
    double VoltageV,
    double CurrentA);

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
