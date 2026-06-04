using System.Runtime.InteropServices;

namespace ReflowOven.Application.Services;

/// <summary>Manutenção backend: storage/category overview, real category clearing and factory reset.</summary>
public sealed class MaintenanceService(IAppDbContext db, IPasswordHasher hasher, IClock clock, ISystemController system, IMasterCredentials master, IAdminCredentials adminCreds, AuditService audit, ICurrentUser current)
{
    /// <summary>Fallback per-row byte estimate, used only if a table's real size can't be read. The category
    /// sizes are the exact <c>pg_total_relation_size</c> of each backing table; inactive users (a row subset
    /// of <c>Users</c>) are sized proportionally; the DB total is the real <c>pg_database_size</c>.</summary>
    private const long BytesPerRecord = 512;

    public async Task<MaintenanceOverviewDto> OverviewAsync(CancellationToken ct = default)
    {
        var exec = await db.Executions.CountAsync(ct);
        var errors = await db.Errors.CountAsync(ct);
        var logs = await db.SystemLog.CountAsync(ct);
        var inativos = await db.Users.CountAsync(u => u.Status == UserStatus.Inativo, ct);
        var totalUsers = await db.Users.IgnoreQueryFilters().CountAsync(ct);

        // Programs split into: active user programs (cleanable) and the soft-deleted ones (the trash, purgeable).
        var totalPrograms = await db.Programs.IgnoreQueryFilters().CountAsync(ct);
        var programas = await db.Programs.CountAsync(p => !p.IsSeed, ct);                          // active, user-created
        var programasDel = await db.Programs.IgnoreQueryFilters().CountAsync(p => p.IsDeleted, ct);
        // Active users a cleanup would remove: everyone active except the caller and the hidden Master.
        var selfId = current.UserId;
        var usuarios = await db.Users.CountAsync(u => u.Type != UserType.Master && u.Status == UserStatus.Ativo && (selfId == null || u.Id != selfId), ct);
        var usuariosDel = await db.Users.IgnoreQueryFilters().CountAsync(u => u.IsDeleted, ct);   // soft-deleted (Lixeira)

        var sizes = await db.GetTableSizesBytesAsync(ct);
        long TableBytes(string table, int count) => sizes.TryGetValue(table, out var b) ? b : count * BytesPerRecord;
        // A row subset of a table is sized as its proportional share of that table's real on-disk size.
        var usersTable = sizes.TryGetValue("Users", out var ub) ? ub : totalUsers * BytesPerRecord;
        var programsTable = sizes.TryGetValue("Programs", out var pb) ? pb : totalPrograms * BytesPerRecord;
        long Share(long table, int part, int total) => total > 0 ? (long)Math.Round(table * (double)part / total) : 0;

        // The Alterações (audit) log is intentionally absent: it is protected — never cleanable from here.
        var categories = new List<CategorySizeDto>
        {
            new(CleanupId.Execucoes, "Execuções", exec, TableBytes("Executions", exec)),
            new(CleanupId.Falhas, "Falhas", errors, TableBytes("Errors", errors)),
            new(CleanupId.Logs, "Logs", logs, TableBytes("SystemLog", logs)),
            new(CleanupId.Inativos, "Usuários inativos", inativos, Share(usersTable, inativos, totalUsers)),
            new(CleanupId.Programas, "Programas salvos", programas, Share(programsTable, programas, totalPrograms)),
            new(CleanupId.Usuarios, "Usuários ativos", usuarios, Share(usersTable, usuarios, totalUsers)),
            new(CleanupId.ProgramasDeletados, "Programas deletados", programasDel, Share(programsTable, programasDel, totalPrograms)),
            new(CleanupId.UsuariosDeletados, "Usuários deletados", usuariosDel, Share(usersTable, usuariosDel, totalUsers)),
        };
        var db_ = new DatabaseSizeDto(await db.GetDatabaseSizeBytesAsync(ct), categories);

        var (freeGB, totalGB) = DiskSpace();
        var metrics = await system.GetMetricsAsync(ct);
        // Real kernel version (uname -r style, e.g. "6.8.0-31-generic") rather than the .NET RuntimeIdentifier —
        // the front shows this under "Versão do Linux".
        return new MaintenanceOverviewDto(db_, metrics.CpuLoadPercent, freeGB, totalGB, RuntimeInformation.OSDescription, metrics.Kernel);
    }

    // Clearing the customer's LIVE data — inactive users, saved (active) programs, active users — is an
    // Admin-only destructive action: the dev Master is read-only there (it only sees the counts/sizes, to
    // inform the Admin). The TRASH (soft-deleted users/programs) is the Master's OWN domain — the MasterOnly
    // Lixeira + per-item restore/purge — so it may bulk-empty those too (e.g. users the Admin deleted);
    // device history (execuções/falhas/logs) stays clearable by Admin AND Master.
    private static readonly CleanupId[] AdminOnlyCategories =
        [CleanupId.Inativos, CleanupId.Programas, CleanupId.Usuarios];

    public async Task<CleanupResultDto> CleanupAsync(IReadOnlyList<CleanupId> categories, CancellationToken ct = default)
    {
        var cats = categories.Distinct().ToList();
        if (current.Role == UserType.Master && cats.Any(AdminOnlyCategories.Contains))
            throw new ForbiddenAppException("Apenas o Admin pode limpar usuários ativos/inativos ou programas salvos.");

        var selfId = current.UserId;
        var deleted = 0;
        foreach (var cat in cats)
        {
            deleted += cat switch
            {
                CleanupId.Execucoes => await db.Executions.ExecuteDeleteAsync(ct),
                // The Alterações (audit) log is protected — reject any attempt to clear it.
                CleanupId.Alteracoes => throw new ValidationAppException("O log de Alterações (auditoria) é protegido e não pode ser apagado."),
                CleanupId.Falhas => await db.Errors.ExecuteDeleteAsync(ct),
                CleanupId.Logs => await db.SystemLog.ExecuteDeleteAsync(ct),
                CleanupId.Inativos => await db.Users.Where(u => u.Status == UserStatus.Inativo).ExecuteDeleteAsync(ct),
                // Saved (active, user-created) programs — the factory seed catalog is kept; favorites cascade.
                CleanupId.Programas => await db.Programs.Where(p => !p.IsSeed).ExecuteDeleteAsync(ct),
                // ACTIVE users only (inativos have their own category), except the caller and the hidden Master
                // (the spare-the-signed-in rule is server-side).
                CleanupId.Usuarios => await db.Users.Where(u => u.Type != UserType.Master && u.Status == UserStatus.Ativo && (selfId == null || u.Id != selfId)).ExecuteDeleteAsync(ct),
                // Empty the program trash: permanently delete every soft-deleted program.
                CleanupId.ProgramasDeletados => await db.Programs.IgnoreQueryFilters().Where(p => p.IsDeleted).ExecuteDeleteAsync(ct),
                // Empty the user trash: permanently delete every soft-deleted user.
                CleanupId.UsuariosDeletados => await db.Users.IgnoreQueryFilters().Where(u => u.IsDeleted).ExecuteDeleteAsync(ct),
                _ => 0,
            };
        }
        audit.Record(OperationType.Limpeza, OperationObject.Configuracao, null,
        [
            OperationField.Of("categorias", string.Join(", ", cats.Select(c => EnumWire.ToWire(c)))),
            OperationField.Of("removidos", deleted),
        ]);
        await db.SaveChangesAsync(ct);
        return new CleanupResultDto(deleted);
    }

    /// <summary>Wipe history + users + programs, then reseed one admin + the dev Master + the factory program + defaults.</summary>
    public async Task FactoryResetAsync(string confirm, CancellationToken ct = default)
    {
        if (confirm != "RESETAR")
            throw new ValidationAppException("Digite RESETAR para confirmar.");

        // History & per-user state.
        await db.LogEvents.ExecuteDeleteAsync(ct);
        await db.Executions.ExecuteDeleteAsync(ct);
        await db.Errors.ExecuteDeleteAsync(ct);
        await db.Changes.ExecuteDeleteAsync(ct);
        await db.SystemLog.ExecuteDeleteAsync(ct);
        await db.OperationLog.ExecuteDeleteAsync(ct);
        // IgnoreQueryFilters so the reset also wipes soft-deleted rows (the global filters hide them otherwise).
        await db.Favorites.IgnoreQueryFilters().ExecuteDeleteAsync(ct);
        await db.PasswordResetTokens.IgnoreQueryFilters().ExecuteDeleteAsync(ct);
        await db.UserActivityStats.IgnoreQueryFilters().ExecuteDeleteAsync(ct);
        await db.Users.IgnoreQueryFilters().ExecuteDeleteAsync(ct);

        // Programs: drop ALL (including hidden seeds) and reseed just the factory default.
        await db.Programs.IgnoreQueryFilters().ExecuteDeleteAsync(ct);
        db.Programs.Add(Defaults.FactoryProgram());

        // Single Admin — config-driven (Admin__*), same as the initial seed.
        db.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            Name = adminCreds.Username,
            Email = adminCreds.Email,
            PasswordHash = hasher.Hash(adminCreds.Password),
            Type = UserType.Admin,
            Status = UserStatus.Ativo,
            CreatedAt = clock.UtcNow,
            MustChangePassword = false,
        });

        // The dev Master superuser survives the reset (it was wiped with the rest above, so recreate it).
        db.Users.Add(Defaults.BuildMasterUser(master, hasher, clock));

        // Settings & calibration back to defaults.
        await db.NotificationSettings.ExecuteDeleteAsync(ct);
        await db.RunSeriesPreferences.ExecuteDeleteAsync(ct);
        await db.Settings.ExecuteDeleteAsync(ct);
        db.Settings.Add(Defaults.Settings());
        await db.Calibrations.ExecuteDeleteAsync(ct);
        db.Calibrations.Add(Defaults.Calibration());

        await db.SaveChangesAsync(ct);

        // The reset itself is audited — the first row of the fresh operation log.
        audit.Record(OperationType.ResetFabrica, OperationObject.Sistema, null, [OperationField.Of("confirmacao", "RESETAR")]);
        await db.SaveChangesAsync(ct);
    }

    private static (double freeGB, double totalGB) DiskSpace()
    {
        try
        {
            var root = Path.GetPathRoot(AppContext.BaseDirectory);
            var drive = new DriveInfo(string.IsNullOrEmpty(root) ? "/" : root);
            return (Math.Round(drive.AvailableFreeSpace / 1e9, 1), Math.Round(drive.TotalSize / 1e9, 1));
        }
        catch
        {
            return (0, 0);
        }
    }
}
