using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ReflowOven.Application.Dtos;

namespace ReflowOven.Infrastructure.Run;

/// <summary>
/// Owns the single active run (singleton). The API drives Start/Stop/GetStatus; the control loop
/// drives <see cref="TickAsync"/>. On a terminal state it persists the <see cref="ExecutionReport"/>
/// (in a fresh scope) and emits the SignalR completion. State changes are serialized with a semaphore.
/// </summary>
public sealed class RunManager(
    IServiceScopeFactory scopeFactory,
    IPowerBoard board,
    ITelemetrySink sink,
    IClock clock,
    ILogger<RunManager> logger) : IRunManager
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ActiveRun? _active;

    public RunStatusDto? GetStatus() => _active is { } run ? BuildStatus(run) : null;

    public async Task<RunStatusDto> StartAsync(string programId, Guid? userId, string? userName, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_active is { Status: RunStatus.Running })
                throw new ConflictException("Já existe uma execução em andamento.");

            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();

            var program = await db.Programs.FirstOrDefaultAsync(p => p.Id == programId, ct)
                ?? throw new NotFoundException("Programa não encontrado.");

            var profile = program.Profile.Count > 0
                ? program.Profile.Select(p => new ProfilePoint { T = p.T, Temp = p.Temp }).ToList()
                : program.Segments is { Count: > 0 } ? ProfileBuilder.ToProfile(program.Segments) : [];
            if (profile.Count == 0)
                throw new ConflictException("O programa não possui um perfil válido.");

            var settings = await db.Settings.FirstOrDefaultAsync(s => s.Id == 1, ct);
            var limits = settings is null
                ? new ProcessLimits(DomainConstants.ConfigTempMax, DomainConstants.ConfigFanRpmMax, DomainConstants.ConfigVoltageMin, DomainConstants.ConfigVoltageMax, DomainConstants.ConfigExtraTimeMax)
                : new ProcessLimits(settings.Oven.MaxTemp, settings.Oven.MaxFanRpm, settings.Voltage.Min, settings.Voltage.Max, settings.Process.MaxExtraTimeSec);

            program.RunCount++;
            program.LastUsed = clock.UtcNow;
            await db.SaveChangesAsync(ct);

            try
            {
                await board.StartProgramAsync(profile, limits, ct);
            }
            catch (Exception ex)
            {
                // Board refused to start: leave _active null (no zombie run) and surface the failure.
                logger.LogError(ex, "Falha ao iniciar o programa '{Program}' na placa.", program.Name);
                throw;
            }

            var run = new ActiveRun
            {
                RunId = Guid.NewGuid(),
                ProgramId = program.Id,
                ProgramName = program.Name,
                UserId = userId,
                UserName = userName,
                Profile = profile,
                StartedAt = clock.UtcNow,
                TotalSeconds = ProfileBuilder.TotalTime(profile),
                Status = RunStatus.Running,
                Phase = RunPhase.Aquecimento,
            };
            _active = run;
            logger.LogInformation("Execução iniciada: '{Program}' por '{User}' (run {RunId}, {Total}s).",
                run.ProgramName, userName ?? "técnico", run.RunId, (int)run.TotalSeconds);
            await sink.PublishStatusAsync(run.RunId, RunStatus.Running);
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
            await FinalizeAsync(run, RunStatus.Aborted, ct);
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

            var elapsed = (clock.UtcNow - run.StartedAt).TotalSeconds;
            var reading = await board.ReadAsync(ct);
            var alvo = ProfileBuilder.TempAt(run.Profile, elapsed);

            var sample = new TraceSample(elapsed, alvo, reading.OvenTempC, reading.BoardTempC, reading.CurrentA, reading.VoltageV, reading.OvenFanRpm, reading.BoardFanRpm);
            run.Last = sample;
            run.Samples.Add(sample);
            run.PeakTemp = Math.Max(run.PeakTemp, reading.OvenTempC);
            run.PeakCurrent = Math.Max(run.PeakCurrent, reading.CurrentA);
            AppendMeasured(run, (int)Math.Round(elapsed), (int)Math.Round(reading.OvenTempC));

            var phase = ProfileBuilder.PhaseAt(run.Profile, elapsed);
            if (phase != run.Phase)
            {
                run.Phase = phase;
                await sink.PublishPhaseAsync(run.RunId, phase);
            }
            await sink.PublishTraceAsync(run.RunId, sample);

            if (elapsed >= run.TotalSeconds)
                await FinalizeAsync(run, RunStatus.Done, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task FinalizeAsync(ActiveRun run, RunStatus status, CancellationToken ct)
    {
        await board.StopAsync(ct);
        run.Status = status;
        var endedAt = clock.UtcNow;
        var duration = (int)Math.Round((endedAt - run.StartedAt).TotalSeconds);

        var report = new ExecutionReport
        {
            Id = run.RunId,
            ProgramId = run.ProgramId,
            ProgramName = run.ProgramName,
            UserId = run.UserId,
            UserName = run.UserName,
            StartedAt = run.StartedAt,
            DurationSeconds = duration,
            Status = status == RunStatus.Done ? ExecutionStatus.Concluido : ExecutionStatus.Falha,
            PeakTemp = (int)Math.Round(run.PeakTemp),
            PeakCurrent = (decimal)Math.Round(run.PeakCurrent, 1),
            CreatedAt = endedAt,
            Points = BuildPoints(run),
            Trace = BuildTrace(run, duration),
            Events =
            [
                new LogEvent { At = run.StartedAt, Kind = LogEventKind.Info, Message = "Execução iniciada", OrderIndex = 0 },
                new LogEvent
                {
                    At = endedAt,
                    Kind = status == RunStatus.Done ? LogEventKind.Info : LogEventKind.Alerta,
                    Message = status == RunStatus.Done ? "Execução concluída" : "Execução abortada",
                    OrderIndex = 1,
                },
            ],
        };

        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            db.Executions.Add(report);
            db.Notifications.Add(new Notification
            {
                Id = Guid.NewGuid(),
                At = endedAt,
                Kind = status == RunStatus.Done ? NotificationFeedKind.Info : NotificationFeedKind.Error,
                Title = status == RunStatus.Done ? "Execução concluída" : "Execução abortada",
                Message = status == RunStatus.Done
                    ? $"'{run.ProgramName}' concluída em {duration}s (pico {report.PeakTemp} °C)."
                    : $"'{run.ProgramName}' foi abortada após {duration}s.",
            });
            await db.SaveChangesAsync(ct);
        }

        _active = null;
        logger.LogInformation("Execução finalizada ({Status}): '{Program}' (run {RunId}, {Duration}s, pico {Peak} °C).",
            status, run.ProgramName, run.RunId, duration, report.PeakTemp);
        await sink.PublishStatusAsync(run.RunId, status);
        await sink.PublishCompletedAsync(run.RunId, report.Id);
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
        var elapsed = (clock.UtcNow - run.StartedAt).TotalSeconds;
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
        public DateTimeOffset StartedAt { get; init; }
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
