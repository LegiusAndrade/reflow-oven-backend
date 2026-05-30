namespace ReflowOven.Infrastructure.Hardware;

/// <summary>
/// Real STM32 power-board driver over RS422. The framing/CRC/command protocol and the serial
/// bridge live in a separate repo; this is a typed stub selected by <c>Hardware:Mode=Rs422</c>.
/// Wrap <c>System.IO.Ports.SerialPort</c> here and implement the protocol.
/// </summary>
public sealed class Rs422PowerBoard : IPowerBoard
{
    private const string Todo = "RS422 power-board protocol não implementado — implemente contra a ponte serial do STM32.";

#pragma warning disable CS0067 // Implemented when the RS422 protocol is wired against the STM32 bridge.
    public event EventHandler<FaultRaised>? FaultRaised;
#pragma warning restore CS0067

    public Task<SensorReadings> ReadAsync(CancellationToken ct = default) => throw new NotSupportedException(Todo);
    public Task StartProgramAsync(IReadOnlyList<ProfilePoint> profile, ProcessLimits limits, CancellationToken ct = default) => throw new NotSupportedException(Todo);
    public Task StopAsync(CancellationToken ct = default) => throw new NotSupportedException(Todo);
    public Task ApplyCalibrationAsync(Calibration calibration, CancellationToken ct = default) => throw new NotSupportedException(Todo);
    public Task ApplyControlConfigAsync(Settings settings, CancellationToken ct = default) => throw new NotSupportedException(Todo);
    public Task<SelfTestResult> RunSelfTestAsync(SelfTestId id, CancellationToken ct = default) => throw new NotSupportedException(Todo);
    public Task<OutputCalStep> DriveOutputAsync(double setVoltage, CancellationToken ct = default) => throw new NotSupportedException(Todo);
    public Task<BoardIdentity> GetIdentityAsync(CancellationToken ct = default) => throw new NotSupportedException(Todo);
}
