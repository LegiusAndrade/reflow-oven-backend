namespace ReflowOven.Infrastructure.Persistence;

/// <summary>Idempotent first-run seeding (called after Database.Migrate). Mirrors the frontend defaults.</summary>
public static partial class DbSeeder
{
    public static async Task SeedAsync(ReflowDbContext db, IPasswordHasher hasher, IClock clock,
        IMasterCredentials master, IAdminCredentials admin, IRegularCredentials regular, CancellationToken ct = default)
    {
        if (!await db.FaultTypes.AnyAsync(ct))
            db.FaultTypes.AddRange(Defaults.FaultTypes());

        if (!await db.Settings.AnyAsync(ct))
            db.Settings.Add(Defaults.Settings());

        if (!await db.Calibrations.AnyAsync(ct))
            db.Calibrations.Add(Defaults.Calibration());

        if (!await db.DeviceInfo.AnyAsync(ct))
        {
            db.DeviceInfo.Add(Defaults.DeviceInfo());
            db.Boards.AddRange(Defaults.Boards());
        }

        if (!await db.Programs.IgnoreQueryFilters().AnyAsync(ct))
        {
            db.Programs.Add(Defaults.FactoryProgram());
            db.Programs.AddRange(Defaults.CatalogPrograms());
        }

        // Accounts. The Admin and the first Regular are config-driven (set real creds via Admin__*/Regular__*
        // in production). Each is idempotent by username, so they also land on an already-seeded DB (like the
        // Master below), not just a fresh one. The extra dev operators (operador1/operador2) are demo-only —
        // seeded by SeedDemoAsync (gated by Seed:Demo), so production gets ONLY Admin/Regular/Master.
        await SeedAccountAsync(db, hasher, clock, admin.Username, admin.Email, admin.Password, UserType.Admin, UserStatus.Ativo, ct);
        await SeedAccountAsync(db, hasher, clock, regular.Username, regular.Email, regular.Password, UserType.Regular, UserStatus.Ativo, ct);

        // The single dev Master superuser — a config-driven account. Create it if missing; otherwise keep its
        // credentials in sync with config/env on each startup, so changing Master__* (e.g. via .env) and
        // restarting actually applies (the password lives in config/env, it is not managed in the DB).
        Validation.ValidateUserName(master.Username);
        Validation.ValidateEmail(master.Email);
        var existingMaster = await db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Type == UserType.Master, ct);
        if (existingMaster is null)
        {
            db.Users.Add(Defaults.BuildMasterUser(master, hasher, clock));
        }
        else
        {
            existingMaster.Name = master.Username;
            existingMaster.Email = master.Email;
            // Re-hash only when the configured password actually changed (avoids a BCrypt hash every startup).
            if (!hasher.Verify(master.Password, existingMaster.PasswordHash))
                existingMaster.PasswordHash = hasher.Hash(master.Password);
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Idempotently seed one account (by username, case-insensitive); no-op if it already exists,
    /// so the config-driven Admin/Regular land on a fresh OR an already-seeded DB without duplicating.</summary>
    private static async Task SeedAccountAsync(ReflowDbContext db, IPasswordHasher hasher, IClock clock,
        string name, string email, string password, UserType type, UserStatus status, CancellationToken ct)
    {
        var lower = name.ToLowerInvariant();
        if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Name.ToLower() == lower, ct)) return;
        // Defense in depth: seed data must obey the same username/email rules as the API.
        Validation.ValidateUserName(name);
        Validation.ValidateEmail(email);
        db.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            Name = name,
            Email = email,
            PasswordHash = hasher.Hash(password),
            Type = type,
            Status = status,
            CreatedAt = clock.UtcNow,
            // Seeded accounts own their password: never force-expire them so the login hard-expiry gate
            // (AuthService) can't lock the admin/operators out.
            MustChangePassword = false,
        });
    }
}
