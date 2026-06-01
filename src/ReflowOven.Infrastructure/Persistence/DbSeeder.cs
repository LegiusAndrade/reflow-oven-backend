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

        // The single dev Master superuser. Guarded independently of the bulk-users seed above, so it also
        // lands on an already-seeded DB (the bulk guard only fires on a fresh one). Idempotent by type.
        if (!await db.Users.AnyAsync(u => u.Type == UserType.Master, ct))
        {
            var m = Defaults.BuildMasterUser(master, hasher, clock);
            Validation.ValidateUserName(m.Name);
            Validation.ValidateEmail(m.Email);
            db.Users.Add(m);
        }

        await db.SaveChangesAsync(ct);
    }
}
