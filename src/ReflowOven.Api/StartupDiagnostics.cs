using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using ReflowOven.Domain.Enums;
using ReflowOven.Infrastructure.Persistence;
using DomainLogLevel = ReflowOven.Domain.Enums.LogLevel;

namespace ReflowOven.Api;

/// <summary>
/// One-shot startup diagnostics emitted to the log (Serilog → console): whether the database had to
/// be created on this run, which migrations were applied, and an inventory ("auditoria") of the
/// persisted/seeded rows (users by status, programs, logs by type). Purely informational.
/// </summary>
public static class StartupDiagnostics
{
    /// <summary>True when the database already exists; false on a fresh server (first run on a new PC).</summary>
    public static async Task<bool> DatabaseExistsAsync(ReflowDbContext db, CancellationToken ct = default)
    {
        var creator = db.GetService<IDatabaseCreator>() as RelationalDatabaseCreator;
        return creator is not null && await creator.ExistsAsync(ct);
    }

    /// <summary>
    /// Applies pending migrations (creating the database when it is missing) and logs the outcome —
    /// a Warning when the database did not exist yet, so a first run on a new machine is obvious in the log.
    /// </summary>
    public static async Task MigrateAndLogAsync(ReflowDbContext db, ILogger logger, CancellationToken ct = default)
    {
        var dbName = db.Database.GetDbConnection().Database;
        var existed = await DatabaseExistsAsync(db, ct);

        // GetPendingMigrationsAsync reads the history table, which doesn't exist yet on a fresh DB;
        // fall back to the full migration list from the assembly in that case.
        var migrations = existed
            ? (await db.Database.GetPendingMigrationsAsync(ct)).ToList()
            : db.Database.GetMigrations().ToList();

        if (!existed)
            logger.LogWarning(
                "Banco de dados \"{Database}\" não existe — criando e migrando na primeira execução ({Count} migração(ões)).",
                dbName, migrations.Count);

        await db.Database.MigrateAsync(ct);

        if (!existed)
            logger.LogInformation("Banco de dados \"{Database}\" criado e migrado com sucesso.", dbName);
        else if (migrations.Count > 0)
            logger.LogInformation(
                "{Count} migração(ões) pendente(s) aplicada(s): {Migrations}.",
                migrations.Count, string.Join(", ", migrations));
        else
            logger.LogInformation("Banco de dados \"{Database}\" já está atualizado (nenhuma migração pendente).", dbName);
    }

    /// <summary>
    /// Logs a one-shot inventory of the database for the operator: users (ativos/inativos/admins),
    /// programs (fábrica/usuário/removidos) and the log-bearing tables (incl. the system log by level).
    /// Never throws — a failure here must not break startup.
    /// </summary>
    public static async Task LogAuditAsync(ReflowDbContext db, ILogger logger, CancellationToken ct = default)
    {
        try
        {
            // Users — by status and role.
            var usersTotal = await db.Users.CountAsync(ct);
            var usersActive = await db.Users.CountAsync(u => u.Status == UserStatus.Ativo, ct);
            var admins = await db.Users.CountAsync(u => u.Type == UserType.Admin, ct);

            // Programs — bypass the soft-delete/seed query filter to count everything.
            var allPrograms = db.Programs.IgnoreQueryFilters();
            var programsTotal = await allPrograms.CountAsync(ct);
            var programsDeleted = await allPrograms.CountAsync(p => p.IsDeleted, ct);
            var programsSeed = await allPrograms.CountAsync(p => p.IsSeed && !p.IsDeleted, ct);
            var programsActive = programsTotal - programsDeleted;
            var programsUser = programsActive - programsSeed;

            // Report / audit tables.
            var executions = await db.Executions.CountAsync(ct);
            var errors = await db.Errors.CountAsync(ct);
            var changes = await db.Changes.CountAsync(ct);
            var notifications = await db.Notifications.CountAsync(ct);

            // System log — by level (INFO / Aviso / Erro).
            var logInfo = await db.SystemLog.CountAsync(l => l.Level == DomainLogLevel.Info, ct);
            var logAviso = await db.SystemLog.CountAsync(l => l.Level == DomainLogLevel.Aviso, ct);
            var logErro = await db.SystemLog.CountAsync(l => l.Level == DomainLogLevel.Erro, ct);
            var logTotal = logInfo + logAviso + logErro;

            logger.LogInformation(
                "Auditoria do banco ▸ Usuários: {Total} (ativos {Active}, inativos {Inactive}, admins {Admins}).",
                usersTotal, usersActive, usersTotal - usersActive, admins);
            logger.LogInformation(
                "Auditoria do banco ▸ Programas: {Total} (ativos {Active} = fábrica {Seed} + usuário {User}; removidos {Deleted}).",
                programsTotal, programsActive, programsSeed, programsUser, programsDeleted);
            logger.LogInformation(
                "Auditoria do banco ▸ Execuções {Executions} · Falhas {Errors} · Alterações {Changes} · Notificações {Notifications}.",
                executions, errors, changes, notifications);
            logger.LogInformation(
                "Auditoria do banco ▸ Log do sistema: {Total} (INFO {Info}, Aviso {Aviso}, Erro {Erro}).",
                logTotal, logInfo, logAviso, logErro);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao gerar a auditoria de inicialização (ignorada).");
        }
    }
}
