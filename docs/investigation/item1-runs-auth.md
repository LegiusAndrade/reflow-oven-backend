# item1-runs-auth

I have everything I need. `ReflowOven.Domain.Enums` is globally imported in the Api project, so `UserType.Regular` is available unqualified in both Program.cs and AuthPolicies usage. Here is the report.

## Files

### /home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Api/Controllers/RunsController.cs
```csharp
namespace ReflowOven.Api.Controllers;

[ApiController]
[Route("api/runs")]
public sealed class RunsController(IRunManager runs, ICurrentUser current) : ControllerBase
{
    /// <summary>Current run status, or null when idle.</summary>
    [HttpGet("status")]
    public ActionResult<RunStatusDto> Status() => Ok(runs.GetStatus());

    [Authorize(Policy = AuthPolicies.AdminOnly)]                                    // line 11
    [HttpPost("start")]
    public Task<RunStatusDto> Start([FromBody] StartRunRequest req, CancellationToken ct)
        => runs.StartAsync(req.ProgramId, current.UserId, current.Name, ct);

    [Authorize(Policy = AuthPolicies.AdminOnly)]                                    // line 16
    [HttpPost("stop")]
    public async Task<ActionResult<RunStatusDto>> Stop(CancellationToken ct) => Ok(await runs.StopAsync(ct));
}
```

### /home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Api/Auth/AuthPolicies.cs
```csharp
namespace ReflowOven.Api.Auth;

/// <summary>Authorization policy names (mirroring the frontend's role/route guards).</summary>
public static class AuthPolicies
{
    /// <summary>Admin-only writes (user CRUD, program mutations, runs, settings, maintenance, …).</summary>
    public const string AdminOnly = "AdminOnly";

    /// <summary>The hidden Calibração tab — requires the technician's "calibration" claim.</summary>
    public const string CalibrationOnly = "CalibrationOnly";
}
```

### /home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Api/Program.cs (authorization policy registration block, lines 99-103)
```csharp
// --- AuthZ (authenticated by default; Admin & Calibration policies) ---------------------
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
    .AddPolicy(AuthPolicies.AdminOnly, p => p.RequireRole(nameof(UserType.Admin)))
    .AddPolicy(AuthPolicies.CalibrationOnly, p => p.RequireClaim("calibration", "true"));
```
Note: `ReflowOven.Domain.Enums` is in `Api/GlobalUsings.cs` (line 10), so `UserType` is usable unqualified here. `nameof(UserType.Admin)` evaluates to the string `"Admin"` and `RequireRole` matches against the `ClaimTypes.Role` claim. (`MapInboundClaims = false` is set on the JWT bearer, so role lookup uses the raw `ClaimTypes.Role` URI as written by `JwtTokenService`.)

### /home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Infrastructure/Auth/JwtTokenService.cs (role + calibration claim wiring)
```csharp
public TokenResult CreateForUser(User user) =>
    Create(user.Id.ToString(), user.Name, user.Type.ToString(), calibration: false, mustChangePassword: user.MustChangePassword);

public TokenResult CreateForCalibration() =>
    Create("calibration", "Calibração", nameof(UserType.Admin), calibration: true, mustChangePassword: false);

private TokenResult Create(string subject, string name, string role, bool calibration, bool mustChangePassword)
{
    ...
    var claims = new List<Claim>
    {
        new(JwtRegisteredClaimNames.Sub, subject),
        new(ClaimTypes.Name, name),
        new(ClaimTypes.Role, role),                 // role = user.Type.ToString() ("Admin"/"Regular"), or "Admin" for technician
        new(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
    };
    if (calibration)
        claims.Add(new Claim(CalibrationClaim, "true"));   // CalibrationClaim = "calibration"
    ...
}
```
Confirmed: a real user's role is `user.Type.ToString()` → `"Admin"` or `"Regular"`. The hidden technician (`CreateForCalibration`) is minted with role `nameof(UserType.Admin)` → `"Admin"` AND the `calibration=true` claim, so the technician already passes any Admin-role policy.

### /home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Domain/Enums/Enums.cs (UserType, lines 11-15)
```csharp
public enum UserType
{
    Admin,
    Regular,
}
```
No `[JsonStringEnumMemberName]` on `UserType` — the wire/DB literals are the identifier names verbatim: `"Admin"` and `"Regular"`.

### RunManager actor logging — /home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Infrastructure/Run/RunManager.cs (lines 24, 64-79)
```csharp
public async Task<RunStatusDto> StartAsync(string programId, Guid? userId, string? userName, CancellationToken ct = default)
{
    ...
    var run = new ActiveRun
    {
        RunId = Guid.NewGuid(),
        ProgramId = program.Id,
        ProgramName = program.Name,
        UserId = userId,
        UserName = userName,
        ...
    };
    _active = run;
    logger.LogInformation("Execução iniciada: '{Program}' por '{User}' (run {RunId}, {Total}s).",
        run.ProgramName, userName ?? "técnico", run.RunId, (int)run.TotalSeconds);
    ...
}
```
The actor (`userId`/`userName`) is taken straight from `current.UserId` / `current.Name` in the controller, logged here, and persisted into `ExecutionReport.UserId`/`UserName` in `FinalizeAsync` (lines 151-152). With the change, a Regular user's name flows through unchanged — no code change required in RunManager. Only behavior difference: the logged/persisted actor can now be a Regular user instead of always Admin/technician. (Note: `StopAsync` does NOT capture the stopping actor; it finalizes with the original starter's `run.UserId`/`run.UserName`. This is pre-existing behavior and out of scope, but worth noting — see open questions.)

## Change plan

1. **File:** `src/ReflowOven.Api/Auth/AuthPolicies.cs` — add a new policy-name constant `OperatorOrAdmin` to the `AuthPolicies` static class.
   - Insert after the `AdminOnly` constant (after line 7):
     ```csharp
     /// <summary>Runs may be started/stopped by an operator (Regular) or an Admin.</summary>
     public const string OperatorOrAdmin = "OperatorOrAdmin";
     ```

2. **File:** `src/ReflowOven.Api/Program.cs` — register the new policy in the `AddAuthorizationBuilder()` chain (lines 100-103). Add one `.AddPolicy(...)` line requiring either role.
   - Old:
     ```csharp
     builder.Services.AddAuthorizationBuilder()
         .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
         .AddPolicy(AuthPolicies.AdminOnly, p => p.RequireRole(nameof(UserType.Admin)))
         .AddPolicy(AuthPolicies.CalibrationOnly, p => p.RequireClaim("calibration", "true"));
     ```
   - New:
     ```csharp
     builder.Services.AddAuthorizationBuilder()
         .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
         .AddPolicy(AuthPolicies.AdminOnly, p => p.RequireRole(nameof(UserType.Admin)))
         .AddPolicy(AuthPolicies.OperatorOrAdmin, p => p.RequireRole(nameof(UserType.Admin), nameof(UserType.Regular)))
         .AddPolicy(AuthPolicies.CalibrationOnly, p => p.RequireClaim("calibration", "true"));
     ```
   - `RequireRole(params string[])` is satisfied if the principal holds ANY of the listed roles, so this admits both `"Admin"` and `"Regular"` (and therefore the technician, who is minted as `"Admin"`). `UserType` is unqualified-available via `Api/GlobalUsings.cs` line 10.

3. **File:** `src/ReflowOven.Api/Controllers/RunsController.cs` — replace the policy on `Start` (line 11).
   - Old: `[Authorize(Policy = AuthPolicies.AdminOnly)]` (the one immediately above `[HttpPost("start")]`)
   - New: `[Authorize(Policy = AuthPolicies.OperatorOrAdmin)]`

4. **File:** `src/ReflowOven.Api/Controllers/RunsController.cs` — replace the policy on `Stop` (line 16).
   - Old: `[Authorize(Policy = AuthPolicies.AdminOnly)]` (the one immediately above `[HttpPost("stop")]`)
   - New: `[Authorize(Policy = AuthPolicies.OperatorOrAdmin)]`
   - Because both attributes are textually identical (`[Authorize(Policy = AuthPolicies.AdminOnly)]`), an Edit tool call must use `replace_all: true` OR include surrounding context (the `[HttpPost("start")]` / `[HttpPost("stop")]` line) to disambiguate. Since both occurrences should become `OperatorOrAdmin`, `replace_all: true` on the single old string is the simplest correct edit.

5. **No change needed** to `RunManager.cs`, `IRunManager.cs`, `JwtTokenService.cs`, `CurrentUser.cs`, or `StartRunRequest`. The actor name already flows from `current.Name` → `StartAsync(userName)` → log + persisted `ExecutionReport`. With Regular users now allowed, the logged `'{User}'` and the stored `ExecutionReport.UserName` will simply reflect the Regular operator instead of always an Admin/technician.

6. **Verify:** run `dotnet build ReflowOven.slnx`. Optionally add/adjust a test asserting that a Regular-role principal is authorized for `/api/runs/start` and `/api/runs/stop` (check `tests/` for existing controller/authorization tests before adding).

## Migration?

**No.** This is purely an ASP.NET Core authorization-policy + attribute change. It touches no entities, columns, DbSets, or enum-to-DB mappings. The `UserType` enum is unchanged (no new members), so the `PtBrEnumConverter`/TEXT column for `UserType` and any CHECK constraints on it are untouched. Nothing is persisted differently. `dotnet ef migrations add` is not required.

## Contract/enum notes

- `UserType` has NO `[JsonStringEnumMemberName]`; its wire/DB literals are the C# identifiers verbatim: `"Admin"` and `"Regular"`. `nameof(UserType.Admin)` / `nameof(UserType.Regular)` produce exactly those strings, matching the role string `JwtTokenService` writes (`user.Type.ToString()`) and what `RequireRole` compares against. Do not introduce a differently-cased literal.
- The frontend already distinguishes Admin vs Regular roles for its own route/menu guards (the policies "mirror the frontend's role/route guards" per the AuthPolicies doc-comment). Allowing Regular to start/stop runs aligns the backend with a frontend that presumably surfaces the Start/Stop controls to operators — confirm the front actually grants Regular access to the run-start UI so the new backend permission isn't broader than the UI intends.
- No DTO shape changes: `StartRunRequest` (in) and `RunStatusDto` (out) are unchanged. The 403 → 200 behavioral change for Regular users is the only observable contract difference; the pt-BR error/conflict strings in `RunManager` (`"Já existe uma execução em andamento."`, etc.) are unaffected.

## Risks / open questions

- **Scope creep on other run-adjacent endpoints.** This only changes `/api/runs/start` and `/api/runs/stop`. If the frontend operator flow needs anything else currently behind `AdminOnly` (e.g. some settings or program selection), those stay 403 for Regular. Confirm only start/stop is intended.
- **Stop actor not captured.** `StopAsync` takes no actor and finalizes with the original starter's `UserId`/`UserName`. So if Admin A starts a run and Regular B stops it, the `ExecutionReport`/abort log still attributes the run to A. Pre-existing; not addressed by this change. If audit-by-stopper matters, that's a separate enhancement (would need `IRunManager.StopAsync` to take an actor + controller to pass `current`).
- **Technician (Calibração) still allowed.** The technician token carries role `"Admin"`, so it continues to pass `OperatorOrAdmin`. Intended (technician was already allowed via `AdminOnly`).
- **Inactive Regular users.** Authorization is role-based only; there is no policy check that the user's `UserStatus` is `Ativo`. A deactivated user with a still-valid (un-expired) JWT could start/stop runs. This already applied to Admins and is not introduced here, but broadening to Regular slightly widens the surface. Tokens expire after `Jwt.ExpiryMinutes`; if stricter revocation is desired it's a separate concern.
- **Tests.** Verify whether existing tests assert a 403 for Regular on these endpoints (they would now need updating to expect 200/authorized). Search `tests/` before applying so a now-stale assertion doesn't break the build.
- **Naming.** Task suggests the name `OperatorOrAdmin`; the codebase models the non-admin role as `Regular` (Portuguese-facing as operator). The constant name `OperatorOrAdmin` is a UI/domain term, not an enum value, so it does not need to match `UserType.Regular`. Acceptable, but if the team prefers enum-aligned naming, `RegularOrAdmin` is an alternative — pick one and use it consistently in `AuthPolicies`, `Program.cs`, and the controller.