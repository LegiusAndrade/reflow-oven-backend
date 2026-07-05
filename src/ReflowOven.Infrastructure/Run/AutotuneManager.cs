using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ReflowOven.Application.Dtos;
using ReflowOven.Application.Services;

namespace ReflowOven.Infrastructure.Run;

/// <summary>
/// Owns the single active PID relay auto-tune (singleton). The API drives Start/Cancel/GetStatus; the control
/// loop drives <see cref="TickAsync"/>. On a terminal state it updates the <see cref="AutotuneRun"/> row (in a
/// fresh scope) and raises a notification + system-log line. Mirrors <see cref="RunManager"/>; mutually
/// exclusive with a run (the firmware NACKs a start while the other is active — the run path relies on that too).
/// </summary>
public sealed class AutotuneManager : IAutotuneManager
{
    private const int MaxSeconds = 1800; // hard monotonic-time guard so a silent board cannot wedge _active forever

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IPowerBoard _board;
    private readonly ISystemLogSink _systemLog;
    private readonly INotificationSink _notifications;
    private readonly IClock _clock;
    private readonly IRunManager _runManager;
    private readonly ILogger<AutotuneManager> _logger;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private ActiveTune? _active;

    public AutotuneManager(IServiceScopeFactory scopeFactory, IPowerBoard board, ISystemLogSink systemLog,
        INotificationSink notifications, IClock clock, IRunManager runManager, ILogger<AutotuneManager> logger)
    {
        _scopeFactory = scopeFactory;
        _board = board;
        _systemLog = systemLog;
        _notifications = notifications;
        _clock = clock;
        _runManager = runManager;
        _logger = logger;
        // Remember the board fault present if a tune FAILS — the 0x0D reply carries only a generic FAILED state.
        _board.FaultRaised += OnBoardFault;
    }

    public bool IsActive => _active is { Row.Status: AutotuneStatus.Executando };

    public AutotuneStatusDto? GetStatus() =>
        _active is { } t ? new AutotuneStatusDto(t.Row.Status == AutotuneStatus.Executando, AutotuneRunDto.From(t.Row)) : null;

    public async Task<AutotuneStatusDto> StartAsync(double targetTemp, Guid? userId, string? userName, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_active is { Row.Status: AutotuneStatus.Executando })
                throw new ConflictException("Já existe um autotune em andamento.");
            if (_runManager.IsRunning)
                throw new ConflictException("Há uma execução em andamento; pare-a antes de calibrar o PID.");

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            var audit = scope.ServiceProvider.GetRequiredService<AuditService>();

            var settings = await db.Settings.FirstOrDefaultAsync(s => s.Id == 1, ct)
                ?? throw new ConflictException("Configurações não inicializadas.");
            if (targetTemp < DomainConstants.PointTempMin || targetTemp > settings.Oven.MaxTemp)
                throw new ValidationAppException(
                    $"Temperatura de oscilação fora da faixa {DomainConstants.PointTempMin}..{settings.Oven.MaxTemp} °C.");

            // Ask the board FIRST: it NACKs if a run/tune is active or it has no config yet, and we must not
            // record a tune that never began (mirrors RunManager.StartAsync ordering).
            try { await _board.AutoTuneAsync(AutoTuneOp.Start, targetTemp, ct); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha ao iniciar o autotune na placa (alvo {Target} °C).", targetTemp);
                throw new ConflictException("A placa recusou o autotune (em execução, ou ainda sem configuração).");
            }

            var row = new AutotuneRun
            {
                Id = Guid.NewGuid(),
                StartedAt = _clock.UtcNow,
                Status = AutotuneStatus.Executando,
                TargetTempC = targetTemp,
                PrevKp = settings.Pid.P,
                PrevKi = settings.Pid.I,
                PrevKd = settings.Pid.D,
                UserId = userId,
                UserName = userName,
                CreatedAt = _clock.UtcNow,
            };
            db.AutotuneRuns.Add(row);
            audit.Record(OperationType.Calibracao, OperationObject.Controlador, row.Id.ToString(),
                [OperationField.Of("evento", "autotune iniciado"), OperationField.Of("alvo_C", targetTemp)],
                operatorId: userId, operatorName: userName ?? "Sistema");
            await db.SaveChangesAsync(ct);

            _active = new ActiveTune { Row = row, StartedTimestamp = _clock.GetTimestamp() };
            _logger.LogInformation("Autotune iniciado: alvo {Target} °C por '{User}' (id {Id}).",
                targetTemp, userName ?? "técnico", row.Id);
            return GetStatus()!;
        }
        finally { _gate.Release(); }
    }

    public async Task<AutotuneStatusDto?> CancelAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_active is not { Row.Status: AutotuneStatus.Executando } tune) return null;
            try { await _board.AutoTuneAsync(AutoTuneOp.Cancel, null, ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "Falha ao enviar CANCEL do autotune à placa (segue o cancelamento)."); }
            await FinalizeAsync(tune, AutotuneStatus.Cancelado, null, "Cancelado pelo operador.", null, ct);
            return GetStatus();
        }
        finally { _gate.Release(); }
    }

    public async Task TickAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_active is not { Row.Status: AutotuneStatus.Executando } tune) return;

            // A board reset / RS422 drop mid-tune is a comms-loss failure (E-130), same as a run.
            if (!_board.IsConnected)
            {
                await FinalizeAsync(tune, AutotuneStatus.Falha, null,
                    "Perda de comunicação RS422 com a placa durante o autotune.", "E-130", ct);
                return;
            }

            // Hard time guard: never let a silent firmware wedge the active tune forever. Monotonic on
            // purpose — an NTP/RTC step must neither falsely expire a live tune nor hold this open.
            if (_clock.GetElapsedTime(tune.StartedTimestamp).TotalSeconds > MaxSeconds)
            {
                try { await _board.AutoTuneAsync(AutoTuneOp.Cancel, null, ct); } catch { /* best-effort */ }
                await FinalizeAsync(tune, AutotuneStatus.Falha, null,
                    $"Tempo máximo de autotune ({MaxSeconds} s) excedido.", tune.LastFault, ct);
                return;
            }

            AutoTuneReadback rb;
            try { rb = await _board.AutoTuneAsync(AutoTuneOp.Query, null, ct); }
            catch (Exception ex) { _logger.LogDebug(ex, "Poll do autotune falhou (segue na próxima tick)."); return; }

            tune.Row.Cycles = rb.Cycles; // live progress (in-memory; persisted at finalize)

            if (rb.State == AutoTuneState.Done)
                await FinalizeAsync(tune, AutotuneStatus.Concluido, rb, null, null, ct);
            else if (rb.State == AutoTuneState.Failed)
            {
                var reason = tune.LastFault is { } fc
                    ? $"Falha da placa durante o autotune ({fc})."
                    : "Não convergiu (sem oscilação utilizável ou tempo esgotado).";
                await FinalizeAsync(tune, AutotuneStatus.Falha, null, reason, tune.LastFault, ct);
            }
        }
        finally { _gate.Release(); }
    }

    private async Task FinalizeAsync(ActiveTune tune, AutotuneStatus status, AutoTuneReadback? result,
        string? reason, string? faultCode, CancellationToken ct)
    {
        // Duration is monotonic (jump-immune). Derive the stored end as StartedAt + duration (not wall-clock
        // now) so a backward clock step mid-tune never persists a FinishedAt before StartedAt.
        var duration = (int)Math.Round(_clock.GetElapsedTime(tune.StartedTimestamp).TotalSeconds);
        var endedAt = tune.Row.StartedAt.AddSeconds(duration);
        var failed = status == AutotuneStatus.Falha;

        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            var audit = scope.ServiceProvider.GetRequiredService<AuditService>();

            var row = await db.AutotuneRuns.FirstOrDefaultAsync(r => r.Id == tune.Row.Id, ct);
            if (row is null) { _active = null; return; } // already gone (factory reset?) — nothing to finalize

            row.Status = status;
            row.FinishedAt = endedAt;
            row.DurationSeconds = duration;
            row.Cycles = result?.Cycles ?? tune.Row.Cycles;
            row.ErrorReason = reason;
            row.FaultCode = faultCode;
            if (result is { } r && status == AutotuneStatus.Concluido)
            {
                row.Ku = Math.Round(r.Ku, 4);
                row.TuMs = r.TuMs;
                row.Kp = Math.Round(r.Kp, 4);
                row.Ki = Math.Round(r.Ki, 4);
                row.Kd = Math.Round(r.Kd, 4);
            }

            var notification = new Notification
            {
                Id = Guid.NewGuid(),
                At = endedAt,
                Kind = failed ? NotificationFeedKind.Error
                    : status == AutotuneStatus.Concluido ? NotificationFeedKind.Info
                    : NotificationFeedKind.Warning,
                Title = status switch
                {
                    AutotuneStatus.Concluido => "Autotune concluído",
                    AutotuneStatus.Cancelado => "Autotune cancelado",
                    _ => "Autotune com falha",
                },
                Message = status == AutotuneStatus.Concluido
                    ? $"PID sugerido: Kp {row.Kp}, Ki {row.Ki}, Kd {row.Kd} (alvo {row.TargetTempC:0.#} °C). Confirme para aplicar."
                    : failed ? $"Autotune falhou após {duration}s: {reason}"
                    : $"Autotune cancelado após {duration}s.",
            };
            db.Notifications.Add(notification);

            var logEntry = new SystemLogEntry
            {
                At = endedAt,
                Level = failed ? ReflowOven.Domain.Enums.LogLevel.Erro
                    : status == AutotuneStatus.Concluido ? ReflowOven.Domain.Enums.LogLevel.Info
                    : ReflowOven.Domain.Enums.LogLevel.Aviso,
                Message = $"{notification.Title}: alvo {row.TargetTempC:0.#} °C, {duration}s"
                    + (status == AutotuneStatus.Concluido
                        ? $" (Kp {row.Kp}, Ki {row.Ki}, Kd {row.Kd})"
                        : reason is { } rr ? $" — {rr}" : ""),
            };
            db.SystemLog.Add(logEntry);

            audit.Record(OperationType.Calibracao, OperationObject.Controlador, row.Id.ToString(),
                [OperationField.Of("status", status), OperationField.Of("duracao_s", duration),
                 OperationField.Of("kp", row.Kp ?? 0), OperationField.Of("ki", row.Ki ?? 0), OperationField.Of("kd", row.Kd ?? 0)],
                operatorId: row.UserId, operatorName: row.UserName ?? "Sistema");

            await db.SaveChangesAsync(ct);
            await _notifications.PublishAsync(NotificationDto.From(notification));
            await _systemLog.PublishAsync(new SystemLogDto(logEntry.Id, logEntry.At, logEntry.Level, logEntry.Message));
        }

        _active = null;
        _logger.LogInformation("Autotune finalizado ({Status}): id {Id}, {Duration}s.", status, tune.Row.Id, duration);
    }

    private void OnBoardFault(object? sender, FaultRaised f)
    {
        if (_active is { } t) t.LastFault = f.FaultTypeCode;
    }

    private sealed class ActiveTune
    {
        public required AutotuneRun Row { get; init; }

        /// <summary>Monotonic start mark (<see cref="IClock.GetTimestamp"/>) — drives the max-duration
        /// guard and the persisted duration, immune to wall-clock steps (Row.StartedAt is only the
        /// stored wall-clock timestamp).</summary>
        public long StartedTimestamp { get; init; }

        public string? LastFault { get; set; }
    }
}
