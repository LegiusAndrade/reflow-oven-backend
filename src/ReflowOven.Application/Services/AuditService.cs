namespace ReflowOven.Application.Services;

/// <summary>
/// Emits audit rows — the per-program/config <see cref="ChangeLogEntry"/> (Relatórios → Alterações, with the
/// rich before/after diff) AND the universal <see cref="OperationLogEntry"/> (Log de Operação) — and bumps
/// per-user activity counters. Methods only stage the changes on the context; the calling service owns the
/// SaveChanges so the whole mutation is one unit. The change methods write BOTH logs, so a program or config
/// edit appears in Alterações (detailed) and in the Log de Operação (summary row).
/// </summary>
public sealed class AuditService(IAppDbContext db, IClock clock, ICurrentUser current, ILogger<AuditService> logger)
{
    /// <summary>Increment the current user's counter for the given pt-BR activity label.</summary>
    public async Task BumpActivityAsync(string label, CancellationToken ct = default)
    {
        var uid = current.UserId;
        if (uid is null) return; // technician/anonymous: no per-user counter
        if (current.Role == UserType.Master) return; // the hidden Master accrues no visible activity counters

        // The caller's token can outlive its user row — the account was purged, or a dev re-seed gave users
        // new ids — so a stale id here would fail the WHOLE mutation with a FK violation. The counter is a
        // secondary stat: if the user no longer exists, just skip it rather than crash the primary action.
        if (!await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == uid, ct)) return;

        var stat = await db.UserActivityStats.FirstOrDefaultAsync(s => s.UserId == uid && s.Label == label, ct);
        if (stat is null)
        {
            stat = new UserActivityStat { UserId = uid.Value, Label = label, Count = 0 };
            db.UserActivityStats.Add(stat);
        }
        stat.Count++;
    }

    /// <summary>
    /// Stage one row on the universal operation log. The operator defaults to the authenticated user; pass
    /// <paramref name="operatorId"/>/<paramref name="operatorName"/> for system or run-context events (a
    /// background tick, a login — where there is no signed-in <see cref="ICurrentUser"/> yet). A null operator
    /// is shown as "Sistema". <b>Never put a password value in <paramref name="data"/>.</b> The caller owns
    /// the SaveChanges so this stays in the same unit of work as the mutation it records.
    /// </summary>
    public void Record(
        OperationType type, OperationObject obj, string? objectId,
        IReadOnlyList<OperationField>? data = null, Guid? operatorId = null, string? operatorName = null)
    {
        db.OperationLog.Add(new OperationLogEntry
        {
            Id = Guid.NewGuid(),
            At = clock.UtcNow,
            OperatorId = operatorId ?? current.UserId,
            OperatorName = operatorName ?? current.Name ?? "Sistema",
            Category = CategoryFor(type, obj),
            Type = type,
            Object = obj,
            ObjectId = objectId,
            Data = data?.ToList() ?? [],
        });
    }

    public void RecordProgramChange(ChangeAction action, ReflowProgram program, IReadOnlyList<ChangePointRow>? points = null)
    {
        logger.LogInformation("Programa {Action}: '{Target}' por '{Actor}'.", action, program.Name, current.Name ?? "sistema");
        // The dev Master is hidden from Admin-visible screens — its changes (and their counts) go ONLY to the
        // Master-only Log de Operação below, never to the shared Alterações/ChangeLog the Admin reads.
        if (current.Role != UserType.Master)
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
        // Mirror into the universal trail (kept even for the Master — the Log de Operação is Master-only).
        Record(ToOperationType(action), OperationObject.Programa, program.Id, [OperationField.Of("programa", program.Name)]);
    }

    public void RecordConfigChange(IReadOnlyList<string> bullets)
    {
        logger.LogInformation("Configuração alterada por '{Actor}': {Changes}.", current.Name ?? "sistema", string.Join("; ", bullets));
        // Master changes stay out of the Admin-visible Alterações/ChangeLog (and its counts) — only the
        // Master-only Log de Operação keeps them.
        if (current.Role != UserType.Master)
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
        Record(OperationType.Alteracao, OperationObject.Configuracao, null, [.. bullets.Select(b => OperationField.Of("configuração", b))]);
    }

    // Criado → Criacao, Editado → Alteracao, Removido → Remocao.
    private static OperationType ToOperationType(ChangeAction a) => a switch
    {
        ChangeAction.Criado => OperationType.Criacao,
        ChangeAction.Removido => OperationType.Remocao,
        _ => OperationType.Alteracao,
    };

    // Coarse bucket for the front's sub-tabs, derived once at write time (indexed for ?category= filtering).
    private static OperationCategory CategoryFor(OperationType type, OperationObject obj) => type switch
    {
        OperationType.Execucao => OperationCategory.Execucao,
        OperationType.Erro => OperationCategory.Falha, // a board fault feeds the "Falha" sub-tab
        OperationType.Comunicacao => OperationCategory.Comunicacao,
        OperationType.Calibracao => OperationCategory.Calibracao,
        OperationType.Limpeza or OperationType.ResetFabrica => OperationCategory.Manutencao,
        OperationType.Login or OperationType.Logout => OperationCategory.Usuario,
        _ => obj == OperationObject.Usuario ? OperationCategory.Usuario : OperationCategory.Alteracao,
    };
}
