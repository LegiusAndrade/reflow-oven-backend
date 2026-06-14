namespace ReflowOven.Infrastructure.Hardware;

/// <summary>
/// Default board implementation — a self-contained thermal/electrical simulator that needs no
/// serial port (runs on a dev box and on the Pi). During a run it interpolates the programmed
/// profile for the oven temperature and derives plausible board temp / current / voltage / fan
/// readings, with jitter/clamps matching the frontend's useLiveReadings.
/// </summary>
public sealed class SimulatedPowerBoard(IClock clock) : IPowerBoard
{
    private readonly Lock _gate = new();
    private bool _running;
    private DateTimeOffset _startedAt;
    private List<ProfilePoint> _profile = [];

#pragma warning disable CS0067 // The simulator stays on the happy path; faults come from the real board.
    public event EventHandler<FaultRaised>? FaultRaised;
#pragma warning restore CS0067

    /// <summary>The simulated link is always up.</summary>
    public bool IsConnected => true;

    public Task<SensorReadings> ReadAsync(CancellationToken ct = default)
    {
        double ovenSet;
        bool running;
        lock (_gate)
        {
            running = _running;
            ovenSet = _running ? ProfileBuilder.TempAt(_profile, (clock.UtcNow - _startedAt).TotalSeconds) : DomainConstants.StartTemp;
        }

        var oven = Clamp(Wander(ovenSet, 1.5), 0, 300);
        var board = Clamp(Wander(28 + oven * 0.16, 1.5), 0, 200);
        var current = Clamp(Wander(running ? oven / 300.0 * 14 : 0, 1), 0, 60);
        var voltage = Clamp(Wander(current * 13, 2), 0, 250);
        var ovenFan = (int)ToStep(Clamp(Wander(running ? 1000 + oven / 300.0 * 4000 : 1500, 60), 0, 6000), 10);
        var boardFan = (int)ToStep(Clamp(Wander(800 + board / 200.0 * 4000, 60), 0, 6000), 10);

        return Task.FromResult(new SensorReadings(board, boardFan, oven, ovenFan, voltage, current));
    }

    public Task StartProgramAsync(IReadOnlyList<ProfileSegment> segments, IReadOnlyList<ProfilePoint> profile, CancellationToken ct = default)
    {
        lock (_gate)
        {
            _running = true;
            _startedAt = clock.UtcNow;
            _profile = [.. profile];  // the sim interpolates the sampled curve; segments are for the real board
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        lock (_gate) _running = false;
        return Task.CompletedTask;
    }

    public Task ApplyCalibrationAsync(Calibration calibration, CancellationToken ct = default) => Task.CompletedTask;

    public Task ApplyControlConfigAsync(Settings settings, CancellationToken ct = default) => Task.CompletedTask;

    public Task<SelfTestResult> RunSelfTestAsync(SelfTestId id, CancellationToken ct = default) =>
        Task.FromResult(new SelfTestResult(id, Random.Shared.NextDouble() < 0.88 ? SelfTestState.Ok : SelfTestState.Fail));

    public Task<OutputCalStep> DriveOutputAsync(double setVoltage, CancellationToken ct = default) =>
        Task.FromResult(new OutputCalStep(setVoltage, Math.Round(setVoltage * 0.98 + 1 + (Random.Shared.NextDouble() * 2 - 1), 2)));

    public Task<BoardIdentity> GetIdentityAsync(CancellationToken ct = default) =>
        Task.FromResult(new BoardIdentity("Rev.C", "PWR-2024-0042", 1024, "Rev.B", "CTRL-2024-0001", 1024));

    private static double Wander(double v, double spread) => v + (Random.Shared.NextDouble() * 2 - 1) * spread;
    private static double Clamp(double v, double min, double max) => Math.Min(max, Math.Max(min, v));
    private static double ToStep(double v, double step) => Math.Round(v / step) * step;
}
