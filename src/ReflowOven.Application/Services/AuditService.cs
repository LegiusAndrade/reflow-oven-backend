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
