namespace ReflowOven.Application.Services;

/// <summary>
/// Auto-tune history + the apply/dismiss of a finished tune. The live start/cancel/poll lifecycle lives in the
/// singleton <see cref="IAutotuneManager"/>; this scoped service owns the DB reads and the "apply the tuned
/// gains to Settings" action (which also pushes the new config to the board).
/// </summary>
public sealed class AutotuneService(IAppDbContext db, IPowerBoard board, AuditService audit, ILogger<AutotuneService> logger)
{
    public async Task<AutotuneHistoryDto> GetHistoryAsync(int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, DomainConstants.ReportPageSizeMax);
        var q = db.AutotuneRuns.AsNoTracking().OrderByDescending(r => r.StartedAt);
        var total = await q.CountAsync(ct);
        var rows = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new AutotuneHistoryDto(total, page, pageSize, rows.Select(AutotuneRunDto.From).ToList());
    }

    /// <summary>The most recent tune (any status) as an idle status — the screen falls back to this when no
    /// tune is active, so a just-finished result and its pending "aplicar?" prompt stay visible.</summary>
    public async Task<AutotuneStatusDto?> GetLatestAsync(CancellationToken ct = default)
    {
        var last = await db.AutotuneRuns.AsNoTracking().OrderByDescending(r => r.StartedAt).FirstOrDefaultAsync(ct);
        return last is null ? null : new AutotuneStatusDto(false, AutotuneRunDto.From(last));
    }

    /// <summary>Apply a completed tune's gains to the Settings PID and push the new config to the board.</summary>
    public async Task<AutotuneRunDto> ApplyAsync(Guid id, Guid? userId, string? userName, CancellationToken ct = default)
    {
        var run = await db.AutotuneRuns.FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new NotFoundException("Autotune não encontrado.");
        if (run.Status != AutotuneStatus.Concluido || run.Kp is null || run.Ki is null || run.Kd is null)
            throw new ValidationAppException("Só é possível aplicar um autotune concluído.");
        if (run.Applied) return AutotuneRunDto.From(run); // idempotent

        var settings = await db.Settings.FirstOrDefaultAsync(s => s.Id == 1, ct)
            ?? throw new NotFoundException("Configurações não inicializadas.");
        settings.Pid.P = run.Kp.Value;
        settings.Pid.I = run.Ki.Value;
        settings.Pid.D = run.Kd.Value;
        run.Applied = true;
        run.Dismissed = true; // applying also clears the pending prompt

        audit.Record(OperationType.Calibracao, OperationObject.Configuracao, run.Id.ToString(),
            [OperationField.Of("evento", "ganhos do autotune aplicados"),
             OperationField.Of("kp", run.Kp.Value), OperationField.Of("ki", run.Ki.Value), OperationField.Of("kd", run.Kd.Value)],
            operatorId: userId, operatorName: userName ?? "Sistema");
        await db.SaveChangesAsync(ct);

        try { await board.ApplyControlConfigAsync(settings, ct); }
        catch (Exception ex) { logger.LogWarning(ex, "Ganhos do autotune salvos, mas o push à placa falhou."); }

        return AutotuneRunDto.From(run);
    }

    /// <summary>Dismiss a finished tune's "aplicar?" prompt without applying it.</summary>
    public async Task<AutotuneRunDto> DismissAsync(Guid id, CancellationToken ct = default)
    {
        var run = await db.AutotuneRuns.FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new NotFoundException("Autotune não encontrado.");
        run.Dismissed = true;
        await db.SaveChangesAsync(ct);
        return AutotuneRunDto.From(run);
    }
}
