using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ReflowOven.Application.Dtos;
using ReflowOven.Application.Services;

namespace ReflowOven.Infrastructure.Run;

/// <summary>
/// Owns the single active run (singleton). The API drives Start/Stop/GetStatus; the control loop
/// drives <see cref="TickAsync"/>. On a terminal state it persists the <see cref="ExecutionReport"/>
/// (in a fresh scope) and emits the SignalR completion. State changes are serialized with a semaphore.
/// </summary>
public sealed class RunManager : IRunManager
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IPowerBoard _board;
    private readonly ITelemetrySink _sink;
    private readonly ISystemLogSink _systemLog;
    private readonly INotificationSink _notifications;
    private readonly IClock _clock;
    private readonly ILogger<RunManager> _logger;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private ActiveRun? _active;

    // A power-board protection fault is delivered on the RS422 RX thread (OnBoardFault). That thread must NOT
    // touch _active (only mutated under _gate), so the handler just stashes the fault here and the next
    // gate-serialized TickAsync consumes it. Guarded by its own lock (a nullable struct is not read/written
    // atomically); kept separate from _gate so the RX thread never blocks on a long tick.
    private readonly Lock _faultGate = new();
    private FaultRaised? _pendingFault;

    public RunManager(
        IServiceScopeFactory scopeFactory,
        IPowerBoard board,
        ITelemetrySink sink,
        ISystemLogSink systemLog,
        INotificationSink notifications,
        IClock clock,
        ILogger<RunManager> logger)
    {
        _scopeFactory = scopeFactory;
        _board = board;
        _sink = sink;
        _systemLog = systemLog;
        _notifications = notifications;
        _clock = clock;
        _logger = logger;
        // Subscribe once for the manager's lifetime — same pattern as AutotuneManager. Both this and the board
        // are singletons, so this never leaks (no per-run += that would need a matching -=). A board protection
        // fault keeps the RS422 link UP, so FaultRaised is the only signal a run gets for it; without this the
        // event fired into the void during a run and a hardware fault was wrongly recorded as "Concluído".
        _board.FaultRaised += OnBoardFault;
    }

    public RunStatusDto? GetStatus() => _active is { } run ? BuildStatus(run) : null;

    public bool IsRunning => _active is { Status: RunStatus.Running };

    public async Task<RunStatusDto> StartAsync(string programId, Guid? userId, string? userName, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_active is { Status: RunStatus.Running })
                throw new ConflictException("Já existe uma execução em andamento.");

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            var audit = scope.ServiceProvider.GetRequiredService<AuditService>();

            var program = await db.Programs.FirstOrDefaultAsync(p => p.Id == programId, ct)
                ?? throw new NotFoundException("Programa não encontrado.");

            var profile = program.Profile.Count > 0
                ? program.Profile.Select(p => new ProfilePoint { T = p.T, Temp = p.Temp }).ToList()
                : program.Segments is { Count: > 0 } ? ProfileBuilder.ToProfile(program.Segments) : [];
            if (profile.Count == 0)
                throw new ConflictException("O programa não possui um perfil válido.");

            // The power board drives the editable segments (it computes the setpoint itself); a direct-import
            // curve (points, no segments) becomes linear ramps so it still ships as segments.
            var segments = program.Segments is { Count: > 0 }
                ? program.Segments
                : LinearSegmentsFromProfile(profile);

            // Logical stages for the report's Comparativo do Perfil: one per editable segment when the
            // program defines them (parabola sub-points collapsed to a single leg), otherwise each stored
            // profile vertex after the t=0 baseline.
            var stages = program.Segments is { Count: > 0 }
                ? ProfileBuilder.StageBoundaries(program.Segments)
                : [.. profile.Where(p => p.T > 0)];

            // Count the run only AFTER the board accepts it — a board that refuses to start must not inflate
            // RunCount/LastUsed (and the "mais usados" ranking) for an execution that never happened.
            try
            {
                await _board.StartProgramAsync(segments, profile, ct);
            }
            catch (Exception ex)
            {
                // Board refused to start: leave _active null (no zombie run) and surface the failure.
                _logger.LogError(ex, "Falha ao iniciar o programa '{Program}' na placa.", program.Name);
                audit.Record(OperationType.Comunicacao, OperationObject.Controlador, program.Id,
                    [OperationField.Of("evento", "falha ao iniciar na placa"), OperationField.Of("erro", ex.Message)],
                    operatorId: userId, operatorName: userName ?? "Sistema");
                try { await db.SaveChangesAsync(ct); } catch { /* best-effort audit; the start failure is the real error */ }
                throw;
            }

            program.RunCount++;
            program.LastUsed = _clock.UtcNow;

            var run = new ActiveRun
            {
                RunId = Guid.NewGuid(),
                ProgramId = program.Id,
                ProgramName = program.Name,
                UserId = userId,
                UserName = userName,
                Profile = profile,
                Stages = stages,
                StartedAt = _clock.UtcNow,
                StartedTimestamp = _clock.GetTimestamp(),
                TotalSeconds = ProfileBuilder.TotalTime(profile),
                Status = RunStatus.Running,
                Phase = RunPhase.Aquecimento,
            };
            _active = run;
            // Drop any board fault left over from before this run (an edge raised while idle) so it can't
            // finalize the fresh run on its very first tick.
            lock (_faultGate) _pendingFault = null;
            audit.Record(OperationType.Execucao, OperationObject.Execucao, run.RunId.ToString(),
                [OperationField.Of("evento", "iniciada"), OperationField.Of("programa", run.ProgramName)],
                operatorId: run.UserId, operatorName: run.UserName ?? "Sistema");
            await db.SaveChangesAsync(ct);
            _logger.LogInformation("Execução iniciada: '{Program}' por '{User}' (run {RunId}, {Total}s).",
                run.ProgramName, userName ?? "técnico", run.RunId, (int)run.TotalSeconds);
            await _sink.PublishStatusAsync(run.RunId, RunStatus.Running);
            return BuildStatus(run);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<RunStatusDto?> StopAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_active is not { Status: RunStatus.Running } run) return null;
            // A manual stop is a user abort, never a catalogued fault (the simulator raises none).
            await FinalizeAsync(run, RunStatus.Aborted, null, ct);
            return BuildStatus(run);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task TickAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_active is not { Status: RunStatus.Running } run) return;

            // A board reset / RS422 drop mid-run breaks the process: the firmware loses the loaded program and
            // the heater goes uncontrolled, so don't keep interpolating a phantom run — finalize it as a
            // comms-loss fault (E-130). IsConnected is debounced by the board's LinkTimeoutMs watchdog, so a
            // single dropped frame won't trip it; only a real ≥LinkTimeoutMs silence (a reset) does.
            if (!_board.IsConnected)
            {
                _logger.LogWarning("Execução '{Program}' (run {RunId}) abortada: link RS422 perdido (E-130).",
                    run.ProgramName, run.RunId);
                await FinalizeAsync(run, RunStatus.Aborted, new FaultInfo(
                    "E-130", ErrorSeverity.Alerta, "Perda de comunicação RS422 com a placa de potência durante a execução.",
                    (int)Math.Round(_clock.GetElapsedTime(run.StartedTimestamp).TotalSeconds), (int)Math.Round(run.Last?.Oven ?? 0)), ct);
                return;
            }

            // A power-board PROTECTION fault (over-temp E-101, thermocouple E-102, over-current E-110,
            // over/under-voltage E-120, NTC E-140, fan E-150, …) leaves the RS422 link UP, so it never trips
            // the comms-loss check above — it arrives asynchronously on the RX thread via FaultRaised and is
            // stashed by OnBoardFault. Consume it here, on the gate-serialized path, and finalize the run as a
            // Falha — exactly like the E-130 case, but with the E-code/severity/message from the fault catalog.
            FaultRaised? pending;
            lock (_faultGate) { pending = _pendingFault; _pendingFault = null; }
            if (pending is { } boardFault)
            {
                var (severity, message) = FaultCatalog.Resolve(boardFault.FaultTypeCode);
                _logger.LogWarning("Execução '{Program}' (run {RunId}) abortada por falha da placa {Code}.",
                    run.ProgramName, run.RunId, boardFault.FaultTypeCode);
                await FinalizeAsync(run, RunStatus.Aborted, new FaultInfo(
                    boardFault.FaultTypeCode, severity, message,
                    (int)Math.Round(_clock.GetElapsedTime(run.StartedTimestamp).TotalSeconds), (int)Math.Round(run.Last?.Oven ?? 0)), ct);
                return;
            }

            // Elapsed comes from the monotonic clock, never from wall-clock subtraction: an NTP/RTC step
            // mid-run would otherwise finish the burn early (or stretch it) — see IClock.GetTimestamp.
            var elapsed = _clock.GetElapsedTime(run.StartedTimestamp).TotalSeconds;
            var reading = await _board.ReadAsync(ct);
            // Prefer the firmware's real setpoint/phase when its controller is driving; fall back to the locally
            // interpolated curve (the simulator and a not-yet-controlling firmware return null).
            var runback = await _board.GetRunStatusAsync(ct);
            var alvo = runback?.SetpointC ?? ProfileBuilder.TempAt(run.Profile, elapsed);

            // Telemetry is streamed and stored at 2 decimals (Alvo is interpolated, so it would otherwise
            // carry a long tail). The peak trackers below read the raw values before their own rounding.
            var sample = new TraceSample(
                Math.Round(elapsed, 2), Math.Round(alvo, 2), Math.Round(reading.OvenTempC, 2), Math.Round(reading.BoardTempC, 2),
                Math.Round(reading.CurrentA, 2), Math.Round(reading.VoltageV, 2), reading.OvenFanRpm, reading.BoardFanRpm);
            run.Last = sample;
            run.Samples.Add(sample);
            run.PeakTemp = Math.Max(run.PeakTemp, reading.OvenTempC);
            run.PeakCurrent = Math.Max(run.PeakCurrent, reading.CurrentA);
            AppendMeasured(run, (int)Math.Round(elapsed), (int)Math.Round(reading.OvenTempC));

            var phase = runback?.Phase ?? ProfileBuilder.PhaseAt(run.Profile, elapsed);
            if (phase != run.Phase)
            {
                run.Phase = phase;
                await _sink.PublishPhaseAsync(run.RunId, phase);
            }
            await _sink.PublishTraceAsync(run.RunId, sample);

            if (elapsed >= run.TotalSeconds)
                await FinalizeAsync(run, RunStatus.Done, null, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task FinalizeAsync(ActiveRun run, RunStatus status, FaultInfo? fault, CancellationToken ct)
    {
        // Any board fault stashed by the RX thread belongs to no run once this finalize completes (the E-130
        // path above can race a FaultRaised, and a manual abort may have stashed one): drop it so it can't
        // leak into the next run. The in-tick fault path already read-and-cleared it; this covers the others.
        lock (_faultGate) _pendingFault = null;

        // Best-effort: a finalize triggered BY a dead/offline board (the comms-loss fault) must still persist
        // the report — don't let an unanswered STOP throw and leave the run un-finalized (it would retry forever).
        try { await _board.StopAsync(ct); }
        catch (Exception ex) { _logger.LogWarning(ex, "Falha ao enviar STOP à placa ao finalizar (segue a finalização)."); }
        run.Status = status;
        // Duration is monotonic (jump-immune). Derive the stored end as StartedAt + duration rather than
        // wall-clock now, so a backward clock step mid-run (an NTP/RTC correction) can never persist an
        // EndAt/CreatedAt BEFORE StartedAt — the stored timeline stays coherent (EndAt ≥ StartedAt always).
        var duration = (int)Math.Round(_clock.GetElapsedTime(run.StartedTimestamp).TotalSeconds);
        var endedAt = run.StartedAt.AddSeconds(duration);

        // A finalize carries a live wire status (done/aborted — the only RunStatus values) plus an optional
        // catalogued fault. A fault makes the persisted execution a Falha regardless of the wire status: a
        // comms-loss/over-temp abort is published live as "aborted" (the frontend has no "failed" state) yet
        // recorded as a Falha with its E-code in Relatórios/Notificações.
        var failed = fault is not null;
        var execStatus = failed ? ExecutionStatus.Falha
            : status == RunStatus.Done ? ExecutionStatus.Concluido
            : ExecutionStatus.Abortado;
        var closingMessage = failed ? "Execução com falha"
            : status == RunStatus.Done ? "Execução concluída"
            : "Execução abortada";

        var report = new ExecutionReport
        {
            Id = run.RunId,
            ProgramId = run.ProgramId,
            ProgramName = run.ProgramName,
            UserId = run.UserId,
            UserName = run.UserName,
            StartedAt = run.StartedAt,
            DurationSeconds = duration,
            Status = execStatus,
            PeakTemp = (int)Math.Round(run.PeakTemp),
            PeakCurrent = (decimal)Math.Round(run.PeakCurrent, 1),
            // Only a real catalogued fault carries when/where it hit + the reason/code; a clean run or a
            // user abort leaves these null (the simulator never produces a fault).
            FaultAtT = fault?.AtT,
            FaultAtTemp = fault?.AtTemp,
            FailureReason = fault?.Message,
            FaultTypeCode = fault?.Code,
            CreatedAt = endedAt,
            Points = BuildPoints(run),
            Comparison = ProfileBuilder.BuildComparison(run.Stages, run.Samples),
            Trace = BuildTrace(run, duration),
            Events =
            [
                new LogEvent { At = run.StartedAt, Kind = LogEventKind.Info, Message = "Execução iniciada", OrderIndex = 0 },
                new LogEvent
                {
                    At = endedAt,
                    Kind = status == RunStatus.Done ? LogEventKind.Info : LogEventKind.Alerta,
                    Message = closingMessage,
                    OrderIndex = 1,
                },
            ],
        };

        // One system-log line per finalize (Log do Sistema): Concluída→Info, Abortada→Aviso, Falha→Erro.
        // Fully-qualified: this file imports Microsoft.Extensions.Logging, whose LogLevel would collide.
        var logLevel = failed ? ReflowOven.Domain.Enums.LogLevel.Erro
            : status == RunStatus.Done ? ReflowOven.Domain.Enums.LogLevel.Info
            : ReflowOven.Domain.Enums.LogLevel.Aviso;
        var logEntry = new SystemLogEntry
        {
            At = endedAt,
            Level = logLevel,
            Message = $"{closingMessage}: '{run.ProgramName}' ({duration}s, pico {report.PeakTemp} °C)"
                + (fault is { } ff ? $" — {ff.Code}" : ""),
        };

        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            var audit = scope.ServiceProvider.GetRequiredService<AuditService>();

            db.SystemLog.Add(logEntry); // saved in the same unit of work; Id is set after SaveChangesAsync.

            // A real fault also produces an ErrorLogEntry so the report can deep-link to it; abort/clean don't.
            if (fault is { } f)
            {
                var error = new ErrorLogEntry
                {
                    Id = Guid.NewGuid(),
                    At = endedAt,
                    FaultTypeCode = f.Code,
                    Severity = f.Severity,
                    Message = f.Message,
                    UserId = run.UserId,
                    UserName = run.UserName,
                    ProgramId = run.ProgramId,
                    ProgramName = run.ProgramName,
                    OvenTemp = (int)Math.Round(run.Last?.Oven ?? 0),
                    PcbTemp = (int)Math.Round(run.Last?.Board ?? 0),
                    StartAt = run.StartedAt,
                    EndAt = endedAt,
                    // InputVoltage stays null: SensorReadings has no mains-voltage channel, and a failure
                    // report must never present a fabricated measurement (the UI renders "—"). VbusV from
                    // the board snapshot is the DC bus, NOT the mains — do not substitute it here.
                    OutputVoltage = (int)Math.Round(run.Last?.Voltage ?? 0),
                    Snapshot = BuildTrace(run, duration),
                    Events = [new LogEvent { At = endedAt, Kind = LogEventKind.Falha, Message = f.Message, OrderIndex = 0 }],
                };
                // Best-effort: pull the board's fault-snapshot buffer (GET_FAULT_SNAPSHOT) and attach it to the
                // error as jsonb. A failed/empty download (the dead board in an E-130, a board with no snapshot,
                // a link timeout) must never stop the Falha from being recorded — log and move on.
                try
                {
                    if (await _board.GetFaultSnapshotAsync(ct) is { } snap)
                        error.BoardSnapshot = MapBoardSnapshot(snap);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Falha ao baixar o snapshot da placa para o erro {Code} (segue o registro).", f.Code);
                }
                db.Errors.Add(error);
                report.LinkedErrorId = error.Id;
            }

            db.Executions.Add(report);
            var notification = new Notification
            {
                Id = Guid.NewGuid(),
                At = endedAt,
                // Concluída → info, abortada → warning (âmbar no front), falha → error.
                Kind = failed ? NotificationFeedKind.Error
                    : status == RunStatus.Done ? NotificationFeedKind.Info
                    : NotificationFeedKind.Warning,
                Title = closingMessage,
                Message = failed ? $"'{run.ProgramName}' falhou após {duration}s ({fault?.Code ?? "sem código"})."
                    : status == RunStatus.Done ? $"'{run.ProgramName}' concluída em {duration}s (pico {report.PeakTemp} °C)."
                    : $"'{run.ProgramName}' foi abortada após {duration}s.",
            };
            db.Notifications.Add(notification);
            audit.Record(OperationType.Execucao, OperationObject.Execucao, run.RunId.ToString(),
                [OperationField.Of("status", execStatus), OperationField.Of("duracao_s", duration), OperationField.Of("pico_C", report.PeakTemp)],
                operatorId: run.UserId, operatorName: run.UserName ?? "Sistema");
            if (fault is { } fa)
                audit.Record(OperationType.Erro, OperationObject.Falha, report.LinkedErrorId?.ToString(),
                    [OperationField.Of("codigo", fa.Code), OperationField.Of("motivo", fa.Message)],
                    operatorId: run.UserId, operatorName: run.UserName ?? "Sistema");
            await db.SaveChangesAsync(ct);
            await _notifications.PublishAsync(NotificationDto.From(notification));
        }

        _active = null;
        _logger.LogInformation("Execução finalizada ({Status}): '{Program}' (run {RunId}, {Duration}s, pico {Peak} °C).",
            status, run.ProgramName, run.RunId, duration, report.PeakTemp);
        await _sink.PublishStatusAsync(run.RunId, status);
        await _sink.PublishCompletedAsync(run.RunId, report.Id);
        // Push the system-log line live to the Log do Sistema hub (Id now populated by the save above).
        await _systemLog.PublishAsync(new ReflowOven.Application.Dtos.SystemLogDto(logEntry.Id, logEntry.At, logEntry.Level, logEntry.Message));
    }

    /// <summary>
    /// A power-board protection fault arrives on the RS422 RX thread. It must NOT touch the run state here
    /// (only mutated under <see cref="_gate"/>): stash the first fault of a burst and let the next
    /// <see cref="TickAsync"/> finalize the active run on the serialized path — mirroring how the in-tick
    /// comms-loss (E-130) check already finalizes. A fault raised while idle is dropped at the next StartAsync.
    /// Same shape as <c>AutotuneManager.OnBoardFault</c> (which only records the code for its own poll).
    /// </summary>
    private void OnBoardFault(object? sender, FaultRaised fault)
    {
        lock (_faultGate) _pendingFault ??= fault;
    }

    /// <summary>A real (non-abort) fault descriptor that turns a finalize into a Falha + linked ErrorLogEntry.
    /// Raised either by the in-tick comms-loss (E-130) check or by a power-board protection fault delivered
    /// through <see cref="IPowerBoard.FaultRaised"/> (see <see cref="OnBoardFault"/>); the simulator produces none.</summary>
    private sealed record FaultInfo(string Code, ErrorSeverity Severity, string Message, int AtT, int AtTemp);

    /// <summary>Direct-import programs (a curve sent as points, no segments) become linear ramps so the power
    /// board can drive them as segments: each step (prev→point) is a Linear segment to that temperature.</summary>
    private static List<ProfileSegment> LinearSegmentsFromProfile(IReadOnlyList<ProfilePoint> profile)
    {
        var segs = new List<ProfileSegment>(Math.Max(0, profile.Count - 1));
        for (var i = 1; i < profile.Count; i++)
            segs.Add(new ProfileSegment
            {
                Temp = (int)Math.Round(profile[i].Temp),
                DurationSec = (int)Math.Round(profile[i].T - profile[i - 1].T),
                Ramp = RampShape.Linear,
            });
        return segs;
    }

    private static List<ExecProfilePoint> BuildPoints(ActiveRun run)
    {
        var points = new List<ExecProfilePoint>(run.Measured.Count + run.Profile.Count);
        foreach (var p in run.Profile)
            points.Add(new ExecProfilePoint { T = (int)Math.Round(p.T), Temp = (int)Math.Round(p.Temp), Kind = ProfileRole.Programmed });
        points.AddRange(run.Measured);
        return points;
    }

    /// <summary>Downsamples the captured per-second samples into the fixed multi-signal snapshot the
    /// report chart renders — same names/units/colors as the fault snapshot for visual consistency.</summary>
    private static FailureSnapshot BuildTrace(ActiveRun run, int durationSec)
    {
        var samples = run.Samples;
        if (samples.Count == 0) return new FailureSnapshot { DurationSec = durationSec, Series = [] };

        var idx = DownsampleIndices(samples.Count, DomainConstants.SnapshotSamples);
        double[] Pick(Func<TraceSample, double> sel) => idx.Select(i => Math.Round(sel(samples[i]), 2)).ToArray();

        return new FailureSnapshot
        {
            DurationSec = durationSec,
            Series =
            [
                new SnapshotSeries { Name = "Alvo", Unit = "°C", Color = "#93c5fd", Values = Pick(s => s.Alvo) },
                new SnapshotSeries { Name = "Temp. Grelha", Unit = "°C", Color = "#fbbf24", Values = Pick(s => s.Oven) },
                new SnapshotSeries { Name = "Temp. Dissipador", Unit = "°C", Color = "#a78bfa", Values = Pick(s => s.Board) },
                new SnapshotSeries { Name = "Corrente", Unit = "A", Color = "#f87171", Values = Pick(s => s.Current) },
                new SnapshotSeries { Name = "Tensão", Unit = "V", Color = "#22d3ee", Values = Pick(s => s.Voltage) },
                new SnapshotSeries { Name = "Fan Forno", Unit = "rpm", Color = "#f472b6", Values = Pick(s => s.OvenFan) },
                new SnapshotSeries { Name = "Fan Diss.", Unit = "rpm", Color = "#34d399", Values = Pick(s => s.BoardFan) },
            ],
        };
    }

    /// <summary>Map the board's downloaded fault snapshot (engineering units) to its persisted jsonb shape.</summary>
    private static BoardFaultSnapshot MapBoardSnapshot(FaultSnapshot s) => new()
    {
        SampleIntervalMs = s.SampleIntervalMs,
        TriggerIndex = s.TriggerIndex,
        FaultCode = s.FaultCode,
        Samples = s.Samples.Select(x => new BoardFaultSample
        {
            OvenTempC = x.OvenTempC,
            BoardTempC = x.BoardTempC,
            VbusV = x.VbusV,
            VregV = x.VregV,
            PdV = x.PdV,
            CurrentA = x.CurrentA,
            FanIntakeRpm = x.FanIntakeRpm,
            FanExhaustRpm = x.FanExhaustRpm,
            FanBoardRpm = x.FanBoardRpm,
            DutyIntakePct = x.DutyIntakePct,
            DutyExhaustPct = x.DutyExhaustPct,
            DutyBoardPct = x.DutyBoardPct,
            McuTempC = x.McuTempC,
            VddaV = x.VddaV,
            FaultFlags = x.FaultFlags,
            SetpointC = x.SetpointC,
            BuckDutyPct = x.BuckDutyPct,
            PowerW = x.PowerW,
        }).ToList(),
    };

    /// <summary>Evenly-spaced indices into a <paramref name="count"/>-length series, capped at
    /// <paramref name="max"/> and always including the first and last sample.</summary>
    private static int[] DownsampleIndices(int count, int max)
    {
        if (count <= max) return Enumerable.Range(0, count).ToArray();
        var result = new int[max];
        for (var i = 0; i < max; i++) result[i] = (int)((long)i * (count - 1) / (max - 1));
        return result;
    }

    private static void AppendMeasured(ActiveRun run, int t, int temp)
    {
        run.Measured.Add(new ExecProfilePoint { T = t, Temp = temp, Kind = ProfileRole.Measured });
        if (run.Measured.Count > DomainConstants.RunMeasuredMaxPoints)
            run.Measured = run.Measured.Where((_, i) => i % 2 == 0).ToList(); // decimate
    }

    private RunStatusDto BuildStatus(ActiveRun run)
    {
        var elapsed = _clock.GetElapsedTime(run.StartedTimestamp).TotalSeconds;
        var progress = run.TotalSeconds > 0 ? Math.Min(1, elapsed / run.TotalSeconds) : 0;
        return new RunStatusDto(
            run.RunId, run.ProgramId, run.ProgramName, run.Status, run.Phase, run.StartedAt,
            Math.Max(0, elapsed), run.TotalSeconds, progress,
            run.Last is { } s ? TraceSampleDto.From(s) : null);
    }

    private sealed class ActiveRun
    {
        public Guid RunId { get; init; }
        public string ProgramId { get; init; } = "";
        public string ProgramName { get; init; } = "";
        public Guid? UserId { get; init; }
        public string? UserName { get; init; }
        public List<ProfilePoint> Profile { get; init; } = [];
        /// <summary>Logical stage boundaries (end-time + target temp) for the per-stage Comparativo do Perfil.</summary>
        public List<ProfilePoint> Stages { get; init; } = [];
        public DateTimeOffset StartedAt { get; init; }

        /// <summary>Monotonic start mark (<see cref="IClock.GetTimestamp"/>) — the source for elapsed/duration,
        /// immune to wall-clock steps; <see cref="StartedAt"/> is only the stored wall-clock timestamp.</summary>
        public long StartedTimestamp { get; init; }

        public double TotalSeconds { get; init; }
        public RunStatus Status { get; set; }
        public RunPhase Phase { get; set; }
        public TraceSample? Last { get; set; }
        public double PeakTemp { get; set; }
        public double PeakCurrent { get; set; }
        public List<ExecProfilePoint> Measured { get; set; } = [];
        public List<TraceSample> Samples { get; } = [];
    }
}
