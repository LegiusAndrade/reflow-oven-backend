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
    private AutoTuneState _tuneState;
    private DateTimeOffset _tuneStartedAt;
    private string? _faultCode; // simulated latched fault (null = OK); cleared by AcknowledgeFaultAsync

#pragma warning disable CS0067 // The simulator stays on the happy path; faults come from the real board.
    public event EventHandler<FaultRaised>? FaultRaised;
    public event EventHandler? ConfigRequested; // a simulated board has nothing to pull — never raised
#pragma warning restore CS0067

    /// <summary>The simulated link is always up.</summary>
    public bool IsConnected => true;

    public Task<SensorReadings> ReadAsync(CancellationToken ct = default)
    {
        double ovenSet;
        bool running;
        string? fault;
        lock (_gate)
        {
            running = _running;
            ovenSet = _running ? ProfileBuilder.TempAt(_profile, (clock.UtcNow - _startedAt).TotalSeconds) : DomainConstants.StartTemp;
            fault = _faultCode;
        }

        var oven = Clamp(Wander(ovenSet, 1.5), 0, 300);
        var board = Clamp(Wander(28 + oven * 0.16, 1.5), 0, 200);
        var current = Clamp(Wander(running ? oven / 300.0 * 14 : 0, 1), 0, 60);
        var voltage = Clamp(Wander(current * 13, 2), 0, 250);
        var ovenFan = (int)ToStep(Clamp(Wander(running ? 1000 + oven / 300.0 * 4000 : 1500, 60), 0, 6000), 10);
        var boardFan = (int)ToStep(Clamp(Wander(800 + board / 200.0 * 4000, 60), 0, 6000), 10);

        return Task.FromResult(new SensorReadings(board, boardFan, oven, ovenFan, voltage, current, fault));
    }

    /// <summary>The simulator has no separate controller — the run loop interpolates the profile itself.</summary>
    public Task<RunReadback?> GetRunStatusAsync(CancellationToken ct = default) => Task.FromResult<RunReadback?>(null);

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

    /// <summary>Clear the simulated latched fault, returning the board to OK. The simulator stays on the happy
    /// path (it never raises a fault on its own), so this is normally a no-op — it exists to honour the
    /// ACK_FAULT contract and lets the acknowledge flow be exercised without hardware.</summary>
    public Task AcknowledgeFaultAsync(CancellationToken ct = default)
    {
        lock (_gate) _faultCode = null;
        return Task.CompletedTask;
    }

    /// <summary>A synthetic but plausible fault snapshot for dev/test: a steady thermal/electrical ramp with a
    /// sharp excursion at the trigger (oven overshoot + over-current spike), one sample every 10 ms. The real
    /// board records this around a protection fault; the simulator never faults on its own, so this lets the
    /// download + persistence path be exercised without hardware. Already in engineering units.</summary>
    public Task<FaultSnapshot?> GetFaultSnapshotAsync(CancellationToken ct = default)
    {
        const int total = 300, trigger = 200, overTempFlag = 1 << 2;
        var samples = new List<FaultSnapshotSample>(total);
        for (var i = 0; i < total; i++)
        {
            var frac = i / (total - 1.0);
            var bump = Math.Exp(-Math.Pow((i - trigger) / 8.0, 2)); // narrow spike centred on the trigger
            var setpoint = 30 + 215 * frac;                         // the controller's intended ramp
            var oven = setpoint + 48 * bump;                        // measured overshoots into the fault
            var board = 28 + oven * 0.16;
            var vbus = 12 + 150 * frac;
            var current = 1 + 13 * frac + 26 * bump;                // over-current excursion at the fault
            var post = i >= trigger;
            samples.Add(new FaultSnapshotSample(
                OvenTempC: Math.Round(oven, 1),
                BoardTempC: Math.Round(board, 1),
                VbusV: Math.Round(vbus, 2),
                VregV: 12.0,
                PdV: 20.0,
                CurrentA: Math.Round(current, 2),
                FanIntakeRpm: (int)(1000 + 4000 * frac),
                FanExhaustRpm: (int)(1000 + 3800 * frac),
                FanBoardRpm: (int)(800 + 3200 * frac),
                DutyIntakePct: (int)(100 * frac),
                DutyExhaustPct: (int)(95 * frac),
                DutyBoardPct: (int)(90 * frac),
                McuTempC: Math.Round(40 + 10 * frac, 1),
                VddaV: 3.3,
                FaultFlags: post ? overTempFlag : 0,
                SetpointC: Math.Round(setpoint, 1),
                BuckDutyPct: (int)(90 * frac),
                PowerW: (int)Math.Round(vbus * current)));
        }
        return Task.FromResult<FaultSnapshot?>(
            new FaultSnapshot(FaultSnapshotCodec.SampleIntervalMs, trigger, overTempFlag, BaseMs: 0, samples));
    }

    public Task ApplyCalibrationAsync(Calibration calibration, CancellationToken ct = default) => Task.CompletedTask;

    public Task ApplyControlConfigAsync(Settings settings, CancellationToken ct = default) => Task.CompletedTask;

    public Task<AutoTuneReadback> AutoTuneAsync(AutoTuneOp op, double? targetC = null, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (op == AutoTuneOp.Start) { _tuneState = AutoTuneState.Running; _tuneStartedAt = clock.UtcNow; }
            else if (op == AutoTuneOp.Cancel && _tuneState == AutoTuneState.Running) _tuneState = AutoTuneState.Failed;

            if (_tuneState == AutoTuneState.Running)
            {
                var elapsed = (clock.UtcNow - _tuneStartedAt).TotalSeconds;
                if (elapsed >= 12) _tuneState = AutoTuneState.Done; // the sim "converges" after ~12 s
                else return Task.FromResult(new AutoTuneReadback(AutoTuneState.Running, (int)Math.Min(8, elapsed / 1.5), 0, 0, 0, 0, 0));
            }
            // Plausible Tyreus-Luyben result (Ku 2 V/°C, Tu 60 s) so the dev flow shows real gains.
            return Task.FromResult(_tuneState == AutoTuneState.Done
                ? new AutoTuneReadback(AutoTuneState.Done, 8, 2.0, 60000, 0.625, 0.0047, 5.95)
                : new AutoTuneReadback(_tuneState, 0, 0, 0, 0, 0, 0));
        }
    }

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
