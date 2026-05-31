using System.Runtime.InteropServices;

namespace ReflowOven.Application.Services;

/// <summary>Manutenção backend: storage/category overview, real category clearing and factory reset.</summary>
public sealed class MaintenanceService(IAppDbContext db, IPasswordHasher hasher, IClock clock, ISystemController system)
{
    /// <summary>Fallback per-row byte estimate, used only if a table's real size can't be read. The category
    /// sizes are the exact <c>pg_total_relation_size</c> of each backing table; inactive users (a row subset
    /// of <c>Users</c>) are sized proportionally; the DB total is the real <c>pg_database_size</c>.</summary>
    private const long BytesPerRecord = 512;

    public async Task<MaintenanceOverviewDto> OverviewAsync(CancellationToken ct = default)
    {
        var exec = await db.Executions.CountAsync(ct);
        var changes = await db.Changes.CountAsync(ct);
        var errors = await db.Errors.CountAsync(ct);
        var logs = await db.SystemLog.CountAsync(ct);
        var inativos = await db.Users.CountAsync(u => u.Status == UserStatus.Inativo, ct);
        var totalUsers = await db.Users.CountAsync(ct);

        var sizes = await db.GetTableSizesBytesAsync(ct);
        long TableBytes(string table, int count) => sizes.TryGetValue(table, out var b) ? b : count * BytesPerRecord;
        // Inactive users are a row subset of the Users table, so size them as their share of it.
        var usersTable = sizes.TryGetValue("Users", out var ub) ? ub : totalUsers * BytesPerRecord;
        var inativosBytes = totalUsers > 0 ? (long)Math.Round(usersTable * (double)inativos / totalUsers) : 0;

        var categories = new List<CategorySizeDto>
        {
            new(CleanupId.Execucoes, "Execuções", exec, TableBytes("Executions", exec)),
            new(CleanupId.Alteracoes, "Alterações", changes, TableBytes("Changes", changes)),
            new(CleanupId.Falhas, "Falhas", errors, TableBytes("Errors", errors)),
            new(CleanupId.Logs, "Logs", logs, TableBytes("SystemLog", logs)),
            new(CleanupId.Inativos, "Usuários inativos", inativos, inativosBytes),
        };
        var db_ = new DatabaseSizeDto(await db.GetDatabaseSizeBytesAsync(ct), categories);

        var (freeGB, totalGB) = DiskSpace();
        var cpu = await system.GetCpuLoadPercentAsync(ct);
        return new MaintenanceOverviewDto(db_, cpu, freeGB, totalGB, RuntimeInformation.OSDescription, RuntimeInformation.RuntimeIdentifier);
    }

    public async Task<CleanupResultDto> CleanupAsync(IReadOnlyList<CleanupId> categories, CancellationToken ct = default)
    {
        var deleted = 0;
        foreach (var cat in categories.Distinct())
        {
            deleted += cat switch
            {
                CleanupId.Execucoes => await db.Executions.ExecuteDeleteAsync(ct),
                CleanupId.Alteracoes => await db.Changes.ExecuteDeleteAsync(ct),
                CleanupId.Falhas => await db.Errors.ExecuteDeleteAsync(ct),
                CleanupId.Logs => await db.SystemLog.ExecuteDeleteAsync(ct),
                CleanupId.Inativos => await db.Users.Where(u => u.Status == UserStatus.Inativo).ExecuteDeleteAsync(ct),
                _ => 0,
            };
        }
        return new CleanupResultDto(deleted);
    }

    /// <summary>Wipe history + users + programs, then reseed one admin + the factory program + defaults.</summary>
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
        await db.Favorites.ExecuteDeleteAsync(ct);
        await db.PasswordResetTokens.ExecuteDeleteAsync(ct);
        await db.UserActivityStats.ExecuteDeleteAsync(ct);
        await db.Users.ExecuteDeleteAsync(ct);

        // Programs: drop ALL (including hidden seeds) and reseed just the factory default.
        await db.Programs.IgnoreQueryFilters().ExecuteDeleteAsync(ct);
        db.Programs.Add(Defaults.FactoryProgram());

        // Single admin.
        var admin = Defaults.FactoryAdmin();
        db.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            Name = admin.Name,
            Email = admin.Email,
            PasswordHash = hasher.Hash(Defaults.DefaultDevPassword),
            Type = admin.Type,
            Status = admin.Status,
            CreatedAt = clock.UtcNow,
        });

        // Settings & calibration back to defaults.
        await db.NotificationSettings.ExecuteDeleteAsync(ct);
        await db.RunSeriesPreferences.ExecuteDeleteAsync(ct);
        await db.Settings.ExecuteDeleteAsync(ct);
        db.Settings.Add(Defaults.Settings());
        await db.Calibrations.ExecuteDeleteAsync(ct);
        db.Calibrations.Add(Defaults.Calibration());

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
