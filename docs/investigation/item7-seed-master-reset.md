# item7-seed-master-reset

I have everything needed. The `Type` column is plain `text` with no CHECK constraint, so adding a `Master` enum member needs no migration. The username `dev.pandewilly` passes both the app regex and the DB POSIX check. Let me produce the report.

## Files

**/home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Domain/Enums/Enums.cs** (the `UserType` enum — no `[JsonStringEnumMemberName]`, so wire literal == C# member name)
```csharp
public enum UserType
{
    Admin,
    Regular,
}
```

**/home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Domain/Entities/User.cs** (Type property)
```csharp
public UserType Type { get; set; } = UserType.Regular;
public UserStatus Status { get; set; } = UserStatus.Ativo;
```

**/home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Application/Common/Defaults.cs** (the dev password const, `SeedUser` record, `Users()`, `FactoryAdmin()` — verbatim)
```csharp
/// <summary>Dev password every seeded user (and the factory-reset admin) gets. Change in production.</summary>
public const string DefaultDevPassword = "reflow1234";
```
```csharp
// --- Users (dev parity; password is hashed by the seeder) -----------------------------
public sealed record SeedUser(string Name, string Email, UserType Type, UserStatus Status);

// Usernames must satisfy DomainConstants.UserNameRegex (letters/digits . _ - only — no spaces/specials).
public static List<SeedUser> Users() =>
[
    new("lucas.silva", "lucas@reflow.local", UserType.Admin, UserStatus.Ativo),
    new("vanessa", "vanessa@reflow.local", UserType.Regular, UserStatus.Ativo),
    new("operador1", "op1@reflow.local", UserType.Regular, UserStatus.Ativo),
    new("operador2", "op2@reflow.local", UserType.Regular, UserStatus.Inativo),
];

/// <summary>The single Admin kept after a factory reset.</summary>
public static SeedUser FactoryAdmin() => new("lucas.silva", "lucas@reflow.local", UserType.Admin, UserStatus.Ativo);
```
The `ActivityLabels` array (indices used by AuditService) is at the top of the file:
```csharp
public static readonly string[] ActivityLabels =
[
    "configurações alteradas",
    "usuários criados",
    "usuários alterados",
    "usuários deletados",
    "programas criados",
    "programas alterados",
    "programas deletados",
];
```

**/home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Infrastructure/Persistence/DbSeeder.cs** (the full Users loop — note the `if (!await db.Users.AnyAsync(ct))` guard that only seeds on a fresh DB)
```csharp
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

await db.SaveChangesAsync(ct);
```
Method signature: `public static async Task SeedAsync(ReflowDbContext db, IPasswordHasher hasher, IClock clock, CancellationToken ct = default)`. It's `public static partial class DbSeeder`.

**/home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Application/Services/MaintenanceService.cs** (`FactoryResetAsync` — full; ctor injects `IPasswordHasher hasher, IClock clock`)
```csharp
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
```
Constructor: `public sealed class MaintenanceService(IAppDbContext db, IPasswordHasher hasher, IClock clock, ISystemController system)`.

**/home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Application/Services/UserService.cs** (`CreateAsync` sets `Type = req.Type`; `UpdateAsync` sets `user.Type = req.Type`; `DeleteAsync` and the last-admin guard)
```csharp
// CreateAsync — Type comes straight from the request DTO:
var user = new User
{
    Id = Guid.NewGuid(),
    Name = name,
    Email = email,
    PasswordHash = hasher.Hash(tempPassword),
    Type = req.Type,
    Status = req.Status,
    CreatedAt = now,
    MustChangePassword = true,
    PasswordIssuedAt = now,
};
```
```csharp
// UpdateAsync — Type/Status reassigned from the request:
var willBeActiveAdmin = req.Type == UserType.Admin && req.Status == UserStatus.Ativo;
if (!willBeActiveAdmin && await IsLastActiveAdminAsync(user.Id, ct))
    throw new ConflictException("Deve haver ao menos um administrador ativo.");

user.Email = email;
user.Type = req.Type;
user.Status = req.Status;
```
```csharp
public async Task DeleteAsync(Guid id, CancellationToken ct = default)
{
    var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct)
        ?? throw new NotFoundException("Usuário não encontrado.");

    if (await IsLastActiveAdminAsync(id, ct))
        throw new ConflictException("Não é possível remover o único administrador ativo.");

    db.Users.Remove(user);
    await audit.BumpActivityAsync(Defaults.ActivityLabels[3], ct); // usuários deletados
    await db.SaveChangesAsync(ct);
    logger.LogInformation("Usuário removido: '{Name}' por '{Actor}'.", user.Name, current.Name);
}
```
There is **no** guard on `Type == Master` anywhere — `CreateAsync`/`UpdateAsync` accept whatever `req.Type` the client sends, and `DeleteAsync` deletes any non-last-admin row.

**/home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Application/Dtos/UserDtos.cs** (`Type` is client-supplied on both Create and Update)
```csharp
public sealed record CreateUserRequest(
    string Name,
    string Email,
    UserType Type = UserType.Regular,
    UserStatus Status = UserStatus.Ativo);

/// <summary>Name is immutable, so it is not part of the update. Password is optional (reset).</summary>
public sealed record UpdateUserRequest(
    string Email,
    UserType Type,
    UserStatus Status,
    string? Password = null);
```
**Risk:** because `UserType` is bound directly from JSON, a client could POST/PUT `"type":"Master"` and self-promote to Master unless the service rejects it.

**/home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Infrastructure/Auth/TechnicianCredentials.cs** (the config-based credential pattern to mirror)
```csharp
public sealed class TechnicianOptions
{
    public const string Section = "Technician";

    /// <summary>Frontend default: calibracao / calibra. Override (and hash) in production.</summary>
    public string Username { get; set; } = "calibracao";
    public string Password { get; set; } = "calibra";
}

public sealed class TechnicianCredentials(IOptions<TechnicianOptions> options) : ITechnicianCredentials
{
    public string Username => options.Value.Username;

    public bool Verify(string password) => string.Equals(password, options.Value.Password, StringComparison.Ordinal);
}
```

**/home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Application/Abstractions/ITechnicianCredentials.cs**
```csharp
public interface ITechnicianCredentials
{
    string Username { get; }
    bool Verify(string password);
}
```

**/home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Infrastructure/DependencyInjection.cs** (registration to mirror — lines 39-40)
```csharp
services.Configure<TechnicianOptions>(config.GetSection(TechnicianOptions.Section));
services.AddSingleton<ITechnicianCredentials, TechnicianCredentials>();
```

**/home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Api/appsettings.json** (where the `Technician` block lives; where a `Master` block would go)
```json
"Jwt": {
  "Issuer": "reflow-oven",
  "Audience": "reflow-oven-ui",
  "SigningKey": "dev-only-change-me-please-use-32-bytes-minimum!",
  "ExpiryMinutes": 480
},
"Technician": {
  "Username": "calibracao",
  "Password": "calibra"
},
```

**/home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Infrastructure/Auth/JwtTokenService.cs** (role claim is `user.Type.ToString()`; technician hardcodes `nameof(UserType.Admin)`)
```csharp
public TokenResult CreateForUser(User user) =>
    Create(user.Id.ToString(), user.Name, user.Type.ToString(), calibration: false, mustChangePassword: user.MustChangePassword);
...
new(ClaimTypes.Role, role),
```
So a User row with `Type = UserType.Master` already mints `ClaimTypes.Role = "Master"` with no JwtTokenService change required.

**/home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Api/Program.cs** (authZ policies — lines 100-104)
```csharp
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
    .AddPolicy(AuthPolicies.AdminOnly, p => p.RequireRole(nameof(UserType.Admin)))
    .AddPolicy(AuthPolicies.OperatorOrAdmin, p => p.RequireRole(nameof(UserType.Admin), nameof(UserType.Regular)))
    .AddPolicy(AuthPolicies.CalibrationOnly, p => p.RequireClaim("calibration", "true"));
```

**/home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Infrastructure/Persistence/ReflowDbContext.cs** (User mapping — the only User CHECK constraint is on Name; `Type` is plain text via the global `PtBrEnumConverter`, no enum CHECK)
```csharp
e.HasKey(u => u.Id);
e.Property(u => u.Name).HasMaxLength(DomainConstants.UserNameMaxLength).IsRequired();
e.HasIndex(u => u.Name).IsUnique();
e.ToTable(t => t.HasCheckConstraint("CK_Users_Name", $"\"Name\" ~ '{DomainConstants.UserNameDbCheck}'"));
```
`DomainConstants.UserNameDbCheck = "^[[:alnum:].]+$"` — `dev.pandewilly` (alnum + dot, no spaces) passes both this and `UserNameRegex = "^[\p{L}\p{N}.]+$"`.

**Frontend contract (already shipped, consumes the role verbatim):** `../reflow-oven-front/src/lib/api.ts:107-109` → `export type Role = "Admin" | "Regular" | "Master";` with `UserDto.type` narrowed to `"Admin" | "Regular"` (Master never appears in the Usuários CRUD list). `../reflow-oven-front/src/lib/auth.ts:57-66` → `canAdminister()` returns true for Admin **or** Master; `isMaster()` gates the Diagnóstico → Log tab.

## Change plan

1. **Enums.cs — add the `Master` member.** File `src/ReflowOven.Domain/Enums/Enums.cs`, enum `UserType`:
   - old: `public enum UserType { Admin, Regular, }`
   - new: add `Master` (no `[JsonStringEnumMemberName]` — it must serialize as the literal `"Master"`):
     ```csharp
     public enum UserType
     {
         Admin,
         Regular,
         Master,
     }
     ```
   This alone makes `CreateForUser` emit `ClaimTypes.Role = "Master"` for a Master row.

2. **Program.cs — let Master pass Admin gates.** File `src/ReflowOven.Api/Program.cs`, the `AddPolicy(AuthPolicies.AdminOnly, …)` line:
   - old: `.AddPolicy(AuthPolicies.AdminOnly, p => p.RequireRole(nameof(UserType.Admin)))`
   - new: `.AddPolicy(AuthPolicies.AdminOnly, p => p.RequireRole(nameof(UserType.Admin), nameof(UserType.Master)))`
   - Also extend `OperatorOrAdmin` so Master can start/stop runs:
     old `…RequireRole(nameof(UserType.Admin), nameof(UserType.Regular))` → new `…RequireRole(nameof(UserType.Admin), nameof(UserType.Regular), nameof(UserType.Master))`.
   (TODO decision #2: recommend extending `AdminOnly` rather than a separate `MasterOnly`, per the front's `canAdminister`.)

3. **Create a config-bound Master identity (mirror TechnicianCredentials).** New file `src/ReflowOven.Infrastructure/Auth/MasterCredentials.cs` (or fold a `MasterOptions` into the Auth folder):
   ```csharp
   public sealed class MasterOptions
   {
       public const string Section = "Master";
       /// <summary>Dev placeholder: dev.pandewilly / pandewilly. Override (real secret) in production via Master__Password.</summary>
       public string Username { get; set; } = "dev.pandewilly";
       public string Password { get; set; } = "pandewilly";
       public string Email { get; set; } = "master@reflow.local";
   }
   ```
   Expose it to Application via a new abstraction `src/ReflowOven.Application/Abstractions/IMasterCredentials.cs` with `string Username { get; } string Email { get; } string Password { get; }` (mirroring `ITechnicianCredentials`). Implement in Infrastructure: `MasterCredentials(IOptions<MasterOptions>)`. Note: unlike the technician (no DB row), the Master IS a real `User` row, so the seeder needs the plaintext password only to hash it — `IMasterCredentials.Password` is the hash input; never returned in any DTO.

4. **Register the Master options + service.** File `src/ReflowOven.Infrastructure/DependencyInjection.cs`, after the technician lines (39-40):
   ```csharp
   services.Configure<MasterOptions>(config.GetSection(MasterOptions.Section));
   services.AddSingleton<IMasterCredentials, MasterCredentials>();
   ```

5. **appsettings.json — add the dev placeholder block.** File `src/ReflowOven.Api/appsettings.json`, after the `"Technician"` object:
   ```json
   "Master": {
     "Username": "dev.pandewilly",
     "Password": "pandewilly",
     "Email": "master@reflow.local"
   },
   ```
   (Dev-only placeholder, like the `dev-only-change-me…` JWT key and technician creds; real value set via `Master__Password` env in production. Optionally add a fail-fast in `Program.cs` if non-Development is left on `"pandewilly"`, mirroring the JWT-key fail-fast.)

6. **Defaults.cs — expose a `MasterUser()` helper for reuse by both seed paths.** File `src/ReflowOven.Application/Common/Defaults.cs`. Because the username/email/password now come from `IMasterCredentials` (config), the cleanest shape is a small factory the seeder/reset call with the resolved values — or just inline the `User` build in both call sites using `IMasterCredentials`. Recommended: add a `public static User BuildMasterUser(IMasterCredentials creds, IPasswordHasher hasher, IClock clock)` static so DbSeeder and FactoryReset share one definition:
   ```csharp
   public static User BuildMasterUser(IMasterCredentials creds, IPasswordHasher hasher, IClock clock) => new()
   {
       Id = Guid.NewGuid(),
       Name = creds.Username,
       Email = creds.Email,
       PasswordHash = hasher.Hash(creds.Password),
       Type = UserType.Master,
       Status = UserStatus.Ativo,
       CreatedAt = clock.UtcNow,
       MustChangePassword = false, // dev superuser: never force-expire (same rationale as seed users)
   };
   ```
   (If you prefer to keep Defaults free of `IMasterCredentials`/`IPasswordHasher` dependencies, inline this in DbSeeder + MaintenanceService instead.)

7. **DbSeeder.cs — separate idempotent Master guard.** File `src/ReflowOven.Infrastructure/Persistence/DbSeeder.cs`. The existing `if (!await db.Users.AnyAsync(ct))` only fires on a fresh DB, so the Master would never appear on already-seeded installs. Change `SeedAsync`'s signature to also accept `IMasterCredentials master` (and thread it from the caller — find the `DbSeeder.SeedAsync(...)` call site in `Program.cs`/startup and pass the resolved service). Then add a **dedicated** guard, independent of the bulk Users guard:
   ```csharp
   if (!await db.Users.AnyAsync(u => u.Type == UserType.Master, ct))
   {
       var m = Defaults.BuildMasterUser(master, hasher, clock);
       Validation.ValidateUserName(m.Name);
       Validation.ValidateEmail(m.Email);
       db.Users.Add(m);
   }
   ```
   Place it after the existing `if (!await db.Users.AnyAsync(ct)) { … }` block and before `await db.SaveChangesAsync(ct);`. This lands the Master on both fresh and pre-existing DBs.

8. **MaintenanceService.FactoryResetAsync — preserve/recreate the Master.** File `src/ReflowOven.Application/Services/MaintenanceService.cs`. Inject `IMasterCredentials master` into the ctor:
   - old: `public sealed class MaintenanceService(IAppDbContext db, IPasswordHasher hasher, IClock clock, ISystemController system)`
   - new: `public sealed class MaintenanceService(IAppDbContext db, IPasswordHasher hasher, IClock clock, ISystemController system, IMasterCredentials master)`
   Since the method does `await db.Users.ExecuteDeleteAsync(ct)` (wipes ALL users including the Master), recreate the Master right after re-adding the single admin:
   ```csharp
   // Single admin.
   var admin = Defaults.FactoryAdmin();
   db.Users.Add(new User { … });

   // Master (dev superuser) — survives the reset.
   db.Users.Add(Defaults.BuildMasterUser(master, hasher, clock));
   ```
   Update the method's `<summary>` to "…reseed one admin + the Master + the factory program + defaults."

9. **UserService — guard against Type=Master via the API.** File `src/ReflowOven.Application/Services/UserService.cs`:
   - In `CreateAsync`, before building the `User`, reject Master:
     ```csharp
     if (req.Type == UserType.Master)
         throw new ValidationAppException("Tipo de usuário inválido.");
     ```
     (Place after the name/email validation.) This stops a client self-promoting via `"type":"Master"`.
   - In `UpdateAsync`, block both *promoting to* Master and *editing* an existing Master row:
     ```csharp
     if (req.Type == UserType.Master || user.Type == UserType.Master)
         throw new ForbiddenAppException("O usuário Master não pode ser alterado por esta tela.");
     ```
     (Place right after the user is loaded, before reassigning `user.Type`.)
   - In `DeleteAsync`, block deleting the Master:
     ```csharp
     if (user.Type == UserType.Master)
         throw new ForbiddenAppException("O usuário Master não pode ser removido.");
     ```
     (Place after the user is loaded, before the last-admin check.)
   - In `ListAsync`, optionally exclude Master so it never shows in the Usuários grid (the front's `UserDto.type` is narrowed to `Admin|Regular`):
     `db.Users.Where(u => u.Type != UserType.Master)…`. Decide with the owner — hiding it keeps the grid clean and matches the "só vai ter ele e mais ninguém" intent; the Master still logs in normally via AuthService (which doesn't filter by type).

10. **(Optional, item 7 part 3 — out of scope for "part 2")** the `/hubs/systemlog` SignalR push is a separate sub-item; not required for the seed/role work.

## Migration?

**No.** The `Type` column is stored as plain `text` via the global `PtBrEnumConverter<UserType>` (applied in `OnModelCreating`'s loop over non-JSON enum properties). The only CHECK constraint on the `Users` table is `CK_Users_Name` (the username pattern `^[[:alnum:].]+$`); there is **no CHECK constraint enumerating the allowed `Type` values**, so adding the `Master` enum member needs no schema change. The Master username `dev.pandewilly` satisfies `CK_Users_Name` (alnum + dot, no spaces) and the app-level `UserNameRegex`/`ValidateUserName`. No new columns are added (the Master is an ordinary `User` row), so the seed/reset changes are pure data, not schema.

## Contract/enum notes

- **`UserType` has NO `[JsonStringEnumMemberName]`**, so the global `JsonStringEnumConverter` serializes each member by its C# name. Adding `Master` therefore puts the literal string `"Master"` on the wire — exactly what the front's `Role = "Admin" | "Regular" | "Master"` (api.ts:109) and `isMaster(role) => role === "Master"` (auth.ts:65) expect. Do **not** add an attribute or rename it.
- The role on the wire comes from `JwtTokenService.Create(... role: user.Type.ToString() ...)` → `new Claim(ClaimTypes.Role, role)`, and from `SessionDto.Role` (a `UserType`). Both already serialize the enum, so once a Master row exists the front receives `role: "Master"` with **zero** other backend changes.
- `UserDto.type` on the front is narrowed to `"Admin" | "Regular"` (api.ts:197-199) — the Usuários CRUD must never surface a Master. Backend `UserDto.Type` is the full `UserType`, so excluding Master from `ListAsync` (step 9) keeps the contract honest; at minimum, Create/Update must never return a Master through the CRUD.
- The pt-BR wire literals that must stay byte-exact elsewhere (`Concluído`, `Crítico`, `Cabo`/`WiFi`, etc.) are unaffected — `Master` is plain ASCII and English by design, matching the front's union.

## Risks / open questions

- **Direct DTO binding is the main risk:** `CreateUserRequest.Type` / `UpdateUserRequest.Type` deserialize straight from client JSON, so without step 9's guards any authenticated admin could POST/PUT `"type":"Master"` and mint a Master JWT (full Admin + Log tab). The service-level rejection is mandatory, not optional.
- **`IsLastActiveAdminAsync` counts only `Type == Admin`** — a Master does NOT count as an "active admin" there. So if the Master is the only privileged account left, the last-admin guard could still block removing the real admin while the Master quietly retains full power. Consider whether `IsLastActiveAdminAsync` / the "ao menos um administrador ativo" rule should treat Master as an admin. Likely fine to leave as-is (the seeded admin always exists post-reset), but flag it.
- **Where to thread `IMasterCredentials` into `DbSeeder.SeedAsync`:** the method is `static` and currently takes `(db, hasher, clock, ct)`. The call site (startup) must resolve `IMasterCredentials` from DI and pass it; confirm the seeder is invoked from a scope where the service is available (it is — Infrastructure registers it as a singleton).
- **Plaintext placeholder in appsettings.json:** `pandewilly` is a dev-only placeholder exactly like the technician `calibra` and the `dev-only-change-me…` JWT key. Recommend a `Program.cs` fail-fast (refuse to start in non-Development if `Master:Password` is still `pandewilly`), mirroring the existing JWT-key fail-fast, so production can't ship the default Master password. Confirm with the owner whether they want that fail-fast now.
- **Idempotency on rename:** the dedicated guard checks `u.Type == UserType.Master`, not the username. If someone later changes `Master:Username` in config, a *second* Master would NOT be created (guard is satisfied), but the existing row keeps the old name. Decide whether a config username change should rename the existing Master row (extra reconcile logic) or is a non-goal. Recommended non-goal for now.
- **TODO decision #2** (extend `AdminOnly` vs. separate `MasterOnly`): plan picks "extend `AdminOnly` to accept Master" to match the front's `canAdminister`. Confirm the owner agrees before implementing.
- The front cited `dev.pandewilly` / `pandewilly`; these are recorded only in the backend TODO (commit `fe0b2be`) and the front's logo alt-text — not as live credentials anywhere, so there's no existing value to stay in lock-step with beyond the TODO citation.