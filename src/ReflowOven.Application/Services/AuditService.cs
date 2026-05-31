namespace ReflowOven.Application.Services;

/// <summary>
/// Emits ChangeLogEntry audit rows and bumps per-user activity counters. Methods only stage the
/// changes on the context; the calling service owns the SaveChanges so the whole mutation is one unit.
/// </summary>
public sealed class AuditService(IAppDbContext db, IClock clock, ICurrentUser current, ILogger<AuditService> logger)
{
    /// <summary>Increment the current user's counter for the given pt-BR activity label.</summary>
    public async Task BumpActivityAsync(string label, CancellationToken ct = default)
    {
        var uid = current.UserId;
        if (uid is null) return; // technician/anonymous: no per-user counter

        var stat = await db.UserActivityStats.FirstOrDefaultAsync(s => s.UserId == uid && s.Label == label, ct);
        if (stat is null)
        {
            stat = new UserActivityStat { UserId = uid.Value, Label = label, Count = 0 };
            db.UserActivityStats.Add(stat);
        }
        stat.Count++;
    }

    public void RecordProgramChange(ChangeAction action, ReflowProgram program, IReadOnlyList<ChangePointRow>? points = null)
    {
        logger.LogInformation("Programa {Action}: '{Target}' por '{Actor}'.", action, program.Name, current.Name ?? "sistema");
        db.Changes.Add(new ChangeLogEntry
        {
            Id = Guid.NewGuid(),
            At = clock.UtcNow,
            Action = action,
            Target = program.Name,
            UserId = current.UserId,
            UserName = current.Name,
            ProgramId = program.Id,
            DetailKind = ChangeDetailKind.Program,
            Points = points?.ToList() ?? [],
        });
    }

    /// <summary>
    /// Bound a program's change history: keep only the most recent
    /// <see cref="DomainConstants.ChangeRetentionPerProgramMax"/> program-change rows for it and stage the
    /// rest for deletion. Call this right AFTER staging the new row (still unsaved, so not yet in the DB):
    /// we keep (cap − 1) persisted rows plus the just-staged one, landing at exactly the cap after save.
    /// Prevents edit-spam on one program from growing the Alterações table without bound.
    /// </summary>
    public async Task PruneProgramChangesAsync(string programId, CancellationToken ct = default)
    {
        var stale = await db.Changes
            .Where(c => c.ProgramId == programId && c.DetailKind == ChangeDetailKind.Program)
            .OrderByDescending(c => c.At)
            .Skip(DomainConstants.ChangeRetentionPerProgramMax - 1)
            .ToListAsync(ct);
        if (stale.Count > 0) db.Changes.RemoveRange(stale);
    }

    public void RecordConfigChange(IReadOnlyList<string> bullets)
    {
        logger.LogInformation("Configuração alterada por '{Actor}': {Changes}.", current.Name ?? "sistema", string.Join("; ", bullets));
        db.Changes.Add(new ChangeLogEntry
        {
            Id = Guid.NewGuid(),
            At = clock.UtcNow,
            Action = ChangeAction.Editado,
            Target = "Configuração do sistema",
            UserId = current.UserId,
            UserName = current.Name,
            DetailKind = ChangeDetailKind.Config,
            ConfigBullets = [.. bullets],
        });
    }
}
