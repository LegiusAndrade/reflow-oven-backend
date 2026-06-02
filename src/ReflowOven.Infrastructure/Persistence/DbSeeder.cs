namespace ReflowOven.Infrastructure.Persistence;

/// <summary>Idempotent first-run seeding (called after Database.Migrate). Mirrors the frontend defaults.</summary>
public static partial class DbSeeder
{
    public static async Task SeedAsync(ReflowDbContext db, IPasswordHasher hasher, IClock clock, IMasterCredentials master, CancellationToken ct = default)
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

        if (!await db.Users.AnyAsync(ct))
        {
            foreach (var u in Defaults.Users())
            {
                // Defense in depth: seed data must obey the same username/email rules as the API.
                Validation.ValidateUserName(u.Name);
                Validation.ValidateEmail(u.Email);
                db.Users.Add(new User
                {
                    Id = Guid.NewGuid(),
                    Name = u.Name,
                    Email = u.Email,
                    PasswordHash = hasher.Hash(Defaults.DefaultDevPassword),
                    Type = u.Type,
                    Status = u.Status,
                    CreatedAt = clock.UtcNow,
                    // Seed users own their password (the dev "reflow1234"): never force-expire them, so the
                    // hard-expiry login gate (AuthService) can't lock dev/factory logins out.
                    MustChangePassword = false,
                });
            }
        }

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
}
