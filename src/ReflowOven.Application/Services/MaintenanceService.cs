using System.Runtime.InteropServices;

namespace ReflowOven.Application.Services;

/// <summary>Manutenção backend: storage/category overview, real category clearing and factory reset.</summary>
public sealed class MaintenanceService(IAppDbContext db, IPasswordHasher hasher, IClock clock)
{
    /// <summary>Rough per-row byte estimate for the per-category breakdown. The DB <b>total</b> below is the
    /// real on-disk size (<c>pg_database_size</c>); only this category split remains an estimate.</summary>
    private const long BytesPerRecord = 512;

    public async Task<MaintenanceOverviewDto> OverviewAsync(CancellationToken ct = default)
    {
        var exec = await db.Executions.CountAsync(ct);
        var changes = await db.Changes.CountAsync(ct);
        var errors = await db.Errors.CountAsync(ct);
        var logs = await db.SystemLog.CountAsync(ct);
        var inativos = await db.Users.CountAsync(u => u.Status == UserStatus.Inativo, ct);

        var categories = new List<CategorySizeDto>
        {
            new(CleanupId.Execucoes, "Execuções", exec, exec * BytesPerRecord),
            new(CleanupId.Alteracoes, "Alterações", changes, changes * BytesPerRecord),
            new(CleanupId.Falhas, "Falhas", errors, errors * BytesPerRecord),
            new(CleanupId.Logs, "Logs", logs, logs * BytesPerRecord),
            new(CleanupId.Inativos, "Usuários inativos", inativos, inativos * BytesPerRecord),
        };
        var db_ = new DatabaseSizeDto(await db.GetDatabaseSizeBytesAsync(ct), categories);

        var (freeGB, totalGB) = DiskSpace();
        return new MaintenanceOverviewDto(db_, freeGB, totalGB, RuntimeInformation.OSDescription, RuntimeInformation.RuntimeIdentifier);
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
