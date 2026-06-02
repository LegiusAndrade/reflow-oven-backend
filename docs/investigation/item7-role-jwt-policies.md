# item7-role-jwt-policies

I have everything I need. The `User.Type` column is `text` with no enum CHECK constraint — only `CK_Users_Name` exists on the Users table (a name-format regex, unrelated to `Type`).

## Files

### /home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Domain/Enums/Enums.cs
```csharp
public enum UserType
{
    Admin,
    Regular,
}
```
No `[JsonStringEnumMemberName]` on `UserType` — the wire literal equals the C# member name, so adding `Master` serializes as `"Master"`.

### /home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Infrastructure/Auth/JwtTokenService.cs
```csharp
/// <summary>Custom claim present only for the hidden technician session.</summary>
public const string CalibrationClaim = "calibration";

/// <summary>Custom claim: the user is still on the system-issued password and should change it.</summary>
public const string MustChangeClaim = "must_change_password";

public TokenResult CreateForUser(User user) =>
    Create(user.Id.ToString(), user.Name, user.Type.ToString(), calibration: false, mustChangePassword: user.MustChangePassword);

public TokenResult CreateForCalibration() =>
    Create("calibration", "Calibração", nameof(UserType.Admin), calibration: true, mustChangePassword: false);

private TokenResult Create(string subject, string name, string role, bool calibration, bool mustChangePassword)
{
    var o = options.Value;
    var now = clock.UtcNow;
    var expires = now.AddMinutes(o.ExpiryMinutes);

    var claims = new List<Claim>
    {
        new(JwtRegisteredClaimNames.Sub, subject),
        new(ClaimTypes.Name, name),
        new(ClaimTypes.Role, role),
        new(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
    };
    if (calibration)
        claims.Add(new Claim(CalibrationClaim, "true"));
    if (mustChangePassword)
        claims.Add(new Claim(MustChangeClaim, "true"));

    var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(o.SigningKey));
    var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    var token = new JwtSecurityToken(o.Issuer, o.Audience, claims, now.UtcDateTime, expires.UtcDateTime, creds);
    var jwt = new JwtSecurityTokenHandler().WriteToken(token);

    return new TokenResult(jwt, expires, now.ToUnixTimeMilliseconds());
}
```
`role = user.Type.ToString()` — a `Master` user automatically gets `ClaimTypes.Role="Master"` with NO code change.

### /home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Api/Program.cs
JWT bearer config:
```csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ClockSkew = TimeSpan.FromSeconds(30),
        };
        options.Events = new JwtBearerEvents { /* SignalR access_token */ };
    });
```
Note: `MapInboundClaims = false`, and there is **no explicit `RoleClaimType`** set in `TokenValidationParameters`. The token issues role under `ClaimTypes.Role` (`http://schemas.microsoft.com/ws/2008/06/identity/claims/role`), which is the default role claim type, so `RequireRole(...)` matches it.

AddAuthorizationBuilder block (lines 100-104):
```csharp
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
    .AddPolicy(AuthPolicies.AdminOnly, p => p.RequireRole(nameof(UserType.Admin)))
    .AddPolicy(AuthPolicies.OperatorOrAdmin, p => p.RequireRole(nameof(UserType.Admin), nameof(UserType.Regular)))
    .AddPolicy(AuthPolicies.CalibrationOnly, p => p.RequireClaim("calibration", "true"));
```

### /home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Api/Auth/AuthPolicies.cs
```csharp
namespace ReflowOven.Api.Auth;

/// <summary>Authorization policy names (mirroring the frontend's role/route guards).</summary>
public static class AuthPolicies
{
    /// <summary>Admin-only writes (user CRUD, program mutations, runs, settings, maintenance, …).</summary>
    public const string AdminOnly = "AdminOnly";

    /// <summary>Runs may be started/stopped by an operator (Regular) or an Admin.</summary>
    public const string OperatorOrAdmin = "OperatorOrAdmin";

    /// <summary>The hidden Calibração tab — requires the technician's "calibration" claim.</summary>
    public const string CalibrationOnly = "CalibrationOnly";
}
```

### /home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Api/Auth/CurrentUser.cs
```csharp
public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    public string? Id => Principal?.FindFirst("sub")?.Value ?? Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    public Guid? UserId => Guid.TryParse(Id, out var g) ? g : null;

    public string? Name => Principal?.FindFirst(ClaimTypes.Name)?.Value;

    public UserType? Role =>
        Enum.TryParse<UserType>(Principal?.FindFirst(ClaimTypes.Role)?.Value, out var r) ? r : null;

    public bool IsCalibration => Principal?.HasClaim("calibration", "true") ?? false;
}
```
`Role` uses `Enum.TryParse<UserType>(...)` — once `Master` is a `UserType` member, `Role` returns `UserType.Master` automatically; no code change required. There is no `IsAdmin`/`IsMaster` helper today (and none is strictly needed for the policy work). Worth verifying the `ICurrentUser` interface (in Application) for any consumers that branch on `Role` — none touched by this task, but see Risks.

### /home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Application/Dtos/AuthDtos.cs
```csharp
public sealed record SessionDto(
    string Id,
    string Name,
    UserType Role,
    long LoginAt,
    bool? Calibration,
    bool? MustChangePassword = null,
    Theme Theme = Theme.System,
    RunSeriesDto? ChartSeries = null);
```
`Role` is `UserType` and serializes via the global `JsonStringEnumConverter` → member name (`"Master"`). `LoginResult`:
```csharp
public sealed record LoginResult(bool Ok, string? Error, string? Token, DateTimeOffset? ExpiresAt, SessionDto? Session)
{
    public static LoginResult Fail(string error) => new(false, error, null, null, null);
    public static LoginResult Success(string token, DateTimeOffset expiresAt, SessionDto session) =>
        new(true, null, token, expiresAt, session);
}
```

### /home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Api/Controllers/AuthController.cs
`Me` produces `SessionDto.Role` by parsing the role claim, defaulting to `Regular`:
```csharp
var role = Enum.TryParse<UserType>(User.FindFirst(ClaimTypes.Role)?.Value, out var r) ? r : UserType.Regular;
...
return new SessionDto(id, name, role, loginAt, calibration ? true : null, mustChange ? true : null,
    prefs?.Theme ?? Theme.System, prefs?.ChartSeries);
```
`Login` delegates to `auth.LoginAsync` (`AuthService` builds the `SessionDto` there — not in this file). Once `Master` is in the enum, parsing `"Master"` returns `UserType.Master` here automatically.

### EF mapping — /home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Infrastructure/Persistence/ReflowDbContext.cs (lines 221-232)
```csharp
// Store every (non-JSON) enum column as its pt-BR wire text.
foreach (var entityType in b.Model.GetEntityTypes())
{
    if (entityType.IsMappedToJson()) continue;
    foreach (var property in entityType.GetProperties())
    {
        var clr = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
        if (!clr.IsEnum) continue;
        var converter = (ValueConverter)Activator.CreateInstance(typeof(PtBrEnumConverter<>).MakeGenericType(clr))!;
        property.SetValueConverter(converter);
    }
}
```
`User.Type` is mapped as `HasColumnType("text")` (confirmed in `ReflowDbContextModelSnapshot.cs`) via `PtBrEnumConverter<UserType>`. The only Users-table check constraint is `CK_Users_Name` (`"Name" ~ '^[[:alnum:].]+$'`) — there is NO CHECK constraint on `Type`. So `"Master"` is a valid value with no DB change needed.

### Blast radius — every `AuthPolicies.AdminOnly` usage (becomes Master-accessible)
- `UsersController.cs:5` — controller-level: **all** user CRUD (List/Get/Create/Update/Delete).
- `SettingsController.cs:10` — `PUT /api/settings` (Update).
- `MaintenanceController.cs:5` — controller-level: overview, cleanup, factory-reset.
- `NotificationsController.cs:33` — `DELETE /api/notifications` (Clear).
- `DiagnosticsController.cs:14` — `POST /api/diagnostics/self-test`.
- `DiagnosticsController.cs:21` — `NetworkController` (controller-level): `POST /api/network/ping`.
- `SystemController.cs` lines 21,31,35,46,57,65,76,87,95,105 — every system mutation (clock/NTP/network apply/update/reboot/shutdown, etc.).
- `ProgramsController.cs:20,28,33` — program Create / Update / Delete.

`CalibrationOnly` usage: `CalibrationController.cs:5` (claim-based — unaffected by Master).
`OperatorOrAdmin` usage: `RunsController.cs:11,16` — `POST /api/runs/start`, `POST /api/runs/stop`.

## Change plan
1. **`src/ReflowOven.Domain/Enums/Enums.cs`** — enum `UserType`: add the `Master` member.
   old:
   ```csharp
   public enum UserType
   {
       Admin,
       Regular,
   }
   ```
   new:
   ```csharp
   public enum UserType
   {
       Admin,
       Regular,
       Master,
   }
   ```
   (No `[JsonStringEnumMemberName]` — wire literal becomes `"Master"`. Add `Master` last so existing ordinal values of `Admin=0`/`Regular=1` are preserved; harmless here since the column is text, but keeps it clean.)

2. **`src/ReflowOven.Api/Program.cs`** — `AddPolicy(AuthPolicies.AdminOnly, …)`: admit Master.
   old:
   ```csharp
   .AddPolicy(AuthPolicies.AdminOnly, p => p.RequireRole(nameof(UserType.Admin)))
   ```
   new:
   ```csharp
   .AddPolicy(AuthPolicies.AdminOnly, p => p.RequireRole(nameof(UserType.Admin), nameof(UserType.Master)))
   ```

3. **`src/ReflowOven.Api/Program.cs`** — `AddPolicy(AuthPolicies.OperatorOrAdmin, …)`: **must also admit Master** (otherwise a Master is neither Admin nor Regular → denied `runs/start` & `runs/stop`).
   old:
   ```csharp
   .AddPolicy(AuthPolicies.OperatorOrAdmin, p => p.RequireRole(nameof(UserType.Admin), nameof(UserType.Regular)))
   ```
   new:
   ```csharp
   .AddPolicy(AuthPolicies.OperatorOrAdmin, p => p.RequireRole(nameof(UserType.Admin), nameof(UserType.Regular), nameof(UserType.Master)))
   ```

4. **`JwtTokenService.cs`** — **no change.** `CreateForUser` already emits `role = user.Type.ToString()`, so a Master user gets `Role="Master"` for free. `CreateForCalibration` stays `nameof(UserType.Admin)` (technician should not silently become Master). `must_change_password` / `calibration` claim wiring is unaffected.

5. **`CalibrationOnly`** — **no change** (claim-based `RequireClaim("calibration","true")`, orthogonal to Master).

6. **`CurrentUser.cs` / `AuthController.Me` / `AuthDtos.SessionDto`** — **no change.** `Enum.TryParse<UserType>` and the global `JsonStringEnumConverter` handle `Master` automatically. (`Me` defaults to `Regular` on parse failure, but `"Master"` parses fine once the member exists.)

7. **`AuthPolicies.cs`** — optional doc-comment tweak only (mention Master inherits Admin); no functional change required. Skip unless you want the XML doc accurate.

## Migration?
**No.** `User.Type` is a `text` column written via `PtBrEnumConverter<UserType>`; the only constraint on the Users table is `CK_Users_Name` (a `Name` regex), and there is **no CHECK constraint enumerating `Type` values**. Adding `Master` to the C# enum changes no column type, default, or constraint — the model snapshot is identical. `dotnet ef migrations add` would produce an empty migration. So no migration is needed for the role/policy change itself. (Seeding a fixed Master *user row* in DbSeeder — item 7.2 — is data, also not a schema migration.)

## Contract/enum notes
- `UserType` has **NO** `[JsonStringEnumMemberName]` attribute, so the wire literal equals the C# member name: adding `Master` serializes/deserializes as exactly `"Master"` — which is what the frontend expects. Do not add an attribute.
- `SessionDto.Role` (type `UserType`) is the front-facing contract field (`useSession`). It will emit `"Admin"` / `"Regular"` / `"Master"`. The front gates the Log tab on `Role === "Master"` (front-only) — backend does not need a Log-tab gate.
- Keep `Admin`/`Regular` literals unchanged (existing rows/JWTs in flight depend on them).
- Add `Master` as the **last** enum member so the numeric ordinals of `Admin`(0)/`Regular`(1) don't shift (defensive; the column is text anyway).

## Risks / open questions
- **OperatorOrAdmin is the real trap.** It currently lists only `Admin|Regular`. Master is neither, so without step 3 a Master would be **denied** `POST /api/runs/start` and `/stop` — the opposite of "Master inherits all Admin powers." Step 3 is mandatory.
- **Calibration session stays Admin-role.** `CreateForCalibration()` mints `role=Admin`; the technician will NOT become Master. Confirm that is intended (it should be — Master is a real user type, not the hidden tech).
- **Default role on parse failure is `Regular`** in `AuthController.Me`; not a concern for valid `"Master"` tokens, but any place that defaults unknown roles to Regular would silently down-grade a malformed Master token. No action needed.
- **Other `Role`-based branching outside policies:** this task touches only authz policies. If any service (e.g. `UserService` create/update validation, or `AuthService.LoginAsync` building `SessionDto`) special-cases `UserType` values (e.g. "cannot delete the last Admin", role allow-lists on user create), Master may need to be folded in there too. Not in scope for item 7.1 but flag for 7.2 (seeding) — verify `UserService` accepts `Master` as a creatable type and that any "last admin" guard counts Master as admin-equivalent if desired.
- **Frontend enum union:** the front's `UserType` TS union must include `"Master"` or it will reject the session — that is the front-side counterpart, out of scope here but required end-to-end.
- **`RoleClaimType` not explicitly set:** relies on the default (`ClaimTypes.Role`) matching the minted claim. Already true today for Admin/Regular, so Master works identically — no change, just noting the dependency.