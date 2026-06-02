# item2-user-password

I now have a complete picture. The frontend `SessionDto`/`Session` types do NOT carry `mustChangePassword` or `passwordExpired`, and the login path only surfaces `result.error` to the user. This is the full report.

## Files

### /home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Application/Services/UserService.cs
`CreateAsync` (lines 21-65) and the password-relevant part of `UpdateAsync` (82-89):
```csharp
public async Task<UserDto> CreateAsync(CreateUserRequest req, CancellationToken ct = default)
{
    var name = (req.Name ?? "").Trim();
    Validation.ValidateUserName(name);
    var email = (req.Email ?? "").Trim();
    Validation.ValidateEmail(email);

    var lower = name.ToLowerInvariant();
    if (await db.Users.AnyAsync(u => u.Name.ToLower() == lower, ct))
        throw new ConflictException("Já existe um usuário com esse nome.");

    // The system generates the initial password and emails it; the admin never sets or sees it.
    // The user must change it within PasswordChangeWithinDays (enforced softly at login).
    var tempPassword = PasswordGenerator.Generate();
    var now = clock.UtcNow;
    var changeBy = now.AddDays(DomainConstants.PasswordChangeWithinDays);

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
    db.Users.Add(user);
    await audit.BumpActivityAsync(Defaults.ActivityLabels[1], ct); // usuários criados
    await db.SaveChangesAsync(ct);
    logger.LogInformation("Usuário criado: '{Name}' ({Type}) por '{Actor}'.", user.Name, user.Type, current.Name);

    // Best-effort: an email failure must not fail the creation (the user row already exists).
    try
    {
        await emailSender.SendNewUserAsync(user.Email, user.Name, tempPassword, changeBy, ct);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Usuário '{Name}' criado, mas o e-mail de boas-vindas falhou para '{Email}'.", user.Name, user.Email);
    }
    return Map(user);
}
```
`req.Password` is **already ignored** today (the system always generates). Constructor (lines 4-6): `IAppDbContext db, IPasswordHasher hasher, IClock clock, AuditService audit, ICurrentUser current, IEmailSender emailSender, ILogger<UserService> logger`. `UpdateAsync` admin-set-password branch:
```csharp
if (!string.IsNullOrEmpty(req.Password))
{
    Validation.ValidatePassword(req.Password);
    user.PasswordHash = hasher.Hash(req.Password);
    // An admin deliberately set a known password — clear the forced-change cycle.
    user.MustChangePassword = false;
    user.PasswordChangedAt = clock.UtcNow;
}
```
`Map` (lines 145-153) does NOT emit any password field; current `UserDto` shape has no `mustChangePassword`.

### /home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Application/Services/AuthService.cs
Constructor (7-15): `IAppDbContext db, IPasswordHasher hasher, IJwtTokenService jwt, IClock clock, ITechnicianCredentials technician, IEmailSender email, ICurrentUser current, ILogger<AuthService> logger`.

`LoginAsync` (17-86) — the verify path and the current SOFT reminder logic:
```csharp
if (!hasher.Verify(password, user.PasswordHash))
{
    logger.LogWarning("Login falhou: senha incorreta para '{User}'.", user.Name);
    return LoginResult.Fail("Senha incorreta.");
}

var now = clock.UtcNow;
user.LastLogin = now;
user.LoginCount++;

// Soft password-change policy: if still on the system-issued password past the deadline,
// resend the reminder (at most once a day) but still allow the login.
var remind = false;
var due = default(DateTimeOffset);
if (user.MustChangePassword && user.PasswordIssuedAt is { } issued)
{
    due = issued.AddDays(DomainConstants.PasswordChangeWithinDays);
    if (now >= due && (user.LastPasswordReminderAt is null || user.LastPasswordReminderAt < now.AddDays(-1)))
    {
        remind = true;
        user.LastPasswordReminderAt = now;
    }
}
await db.SaveChangesAsync(ct);

logger.LogInformation("Login OK: '{User}' ({Type}).", user.Name, user.Type);
if (remind)
{
    logger.LogWarning("Senha de '{User}' vencida desde {Due:dd/MM/yyyy}; reenviando lembrete.", user.Name, due);
    try { await email.SendPasswordChangeReminderAsync(user.Email, user.Name, due, ct); }
    catch (Exception ex) { logger.LogError(ex, "Falha ao reenviar o lembrete de senha para '{Email}'.", user.Email); }
}

var tok = jwt.CreateForUser(user);
var prefs = UserService.MapPrefs(user.Preferences);
var dto = new SessionDto(user.Id.ToString(), user.Name, user.Type, tok.IssuedAtUnixMs, null,
    user.MustChangePassword ? true : null, prefs.Theme, prefs.ChartSeries);
return LoginResult.Success(tok.Token, tok.ExpiresAt, dto);
```
Exact pt-BR login error literals (all via `LoginResult.Fail`): `"Informe o usuário."` (23), `"Usuário não encontrado."` (41), `"Usuário inativo."` (46), `"Senha incorreta."` (51). The throttle timestamp is `user.LastPasswordReminderAt` (window = `now.AddDays(-1)`); the deadline anchor is `user.PasswordIssuedAt + PasswordChangeWithinDays`.

`ChangePasswordAsync` (89-109):
```csharp
public async Task<OkResponse> ChangePasswordAsync(ChangePasswordRequest req, CancellationToken ct = default)
{
    var uid = current.UserId ?? throw new ForbiddenAppException("Sessão sem usuário (login técnico não troca senha).");
    var user = await db.Users.FirstOrDefaultAsync(u => u.Id == uid, ct)
        ?? throw new NotFoundException("Usuário não encontrado.");

    if (!hasher.Verify(req.CurrentPassword ?? "", user.PasswordHash))
    {
        logger.LogWarning("Troca de senha falhou: senha atual incorreta para '{User}'.", user.Name);
        throw new ValidationAppException("Senha atual incorreta.");
    }
    Validation.ValidatePassword(req.NewPassword ?? "");

    user.PasswordHash = hasher.Hash(req.NewPassword!);
    user.MustChangePassword = false;
    user.PasswordChangedAt = clock.UtcNow;
    await db.SaveChangesAsync(ct);

    logger.LogInformation("Senha alterada por '{User}'.", user.Name);
    return new OkResponse();
}
```
Note: `ChangePasswordAsync` does NOT clear `PasswordIssuedAt`/`LastPasswordReminderAt` (only flips `MustChangePassword=false` + sets `PasswordChangedAt`). The login gate is keyed on `MustChangePassword` so that is sufficient, but a reissue must reset these.

### /home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Domain/Entities/User.cs
All fields (10-47):
```csharp
public Guid Id { get; set; }
public string Name { get; set; } = "";
public string Email { get; set; } = "";
public string PasswordHash { get; set; } = "";
public UserType Type { get; set; } = UserType.Regular;
public UserStatus Status { get; set; } = UserStatus.Ativo;
public DateTimeOffset CreatedAt { get; set; }
/// <summary>Null = never logged in (rendered as "—" on the client).</summary>
public DateTimeOffset? LastLogin { get; set; }
/// <summary>Lifetime successful-login count (feeds the Diagnóstico "top users by logins" ranking).</summary>
public long LoginCount { get; set; }
/// <summary>True from creation until the user changes the password that was emailed to them.</summary>
public bool MustChangePassword { get; set; }
/// <summary>When the current system-issued password was set — anchors the change deadline.</summary>
public DateTimeOffset? PasswordIssuedAt { get; set; }
/// <summary>When the user last set their own password (null = still on the issued one).</summary>
public DateTimeOffset? PasswordChangedAt { get; set; }
/// <summary>Last "please change your password" reminder we emailed (throttle: at most once a day).</summary>
public DateTimeOffset? LastPasswordReminderAt { get; set; }
public ICollection<UserActivityStat> ActivityStats { get; set; } = new List<UserActivityStat>();
public UserPreferences Preferences { get; set; } = new();
```
All four timestamp fields needed for hard-expiry already exist: `PasswordIssuedAt`, `PasswordChangedAt`, `LastPasswordReminderAt`, plus `MustChangePassword`. No new column is strictly required.

### /home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Application/Dtos/UserDtos.cs
```csharp
public sealed record UserDto(
    string Id, string Name, string Email, UserType Type, UserStatus Status,
    DateTimeOffset CreatedAt, DateTimeOffset? LastLogin, IReadOnlyList<UserEventDto> Events);

public sealed record UserEventDto(string Label, int Count);

public sealed record CreateUserRequest(
    string Name, string Email, string Password,
    UserType Type = UserType.Regular, UserStatus Status = UserStatus.Ativo);

public sealed record UpdateUserRequest(
    string Email, UserType Type, UserStatus Status, string? Password = null);
```

### /home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Application/Dtos/AuthDtos.cs
```csharp
public sealed record LoginRequest(string Username, string Password);

public sealed record SessionDto(
    string Id, string Name, UserType Role, long LoginAt, bool? Calibration,
    bool? MustChangePassword = null, Theme Theme = Theme.System, RunSeriesDto? ChartSeries = null);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record LoginResult(bool Ok, string? Error, string? Token, DateTimeOffset? ExpiresAt, SessionDto? Session)
{
    public static LoginResult Fail(string error) => new(false, error, null, null, null);
    public static LoginResult Success(string token, DateTimeOffset expiresAt, SessionDto session) =>
        new(true, null, token, expiresAt, session);
}

public sealed record ForgotPasswordRequest(string Email);
public sealed record OkResponse(bool Ok = true);
```

### /home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Domain/Common/DomainConstants.cs
```csharp
public const int PasswordMinLength = 8;
public const int PasswordMaxLength = 72;

/// <summary>A new user must change the emailed password within this many days; after that, each
/// login resends the reminder email (login is still allowed).</summary>
public const int PasswordChangeWithinDays = 7;

/// <summary>Length of the system-generated initial password emailed to a new user.</summary>
public const int GeneratedPasswordLength = 14;
```
There is NO separate reminder-cadence constant — the "at most once a day" throttle is hard-coded inline as `now.AddDays(-1)` in `AuthService`.

### /home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Application/Common/PasswordGenerator.cs
`PasswordGenerator.Generate(int length = DomainConstants.GeneratedPasswordLength)` — crypto-RNG, unambiguous alphabet, guarantees one upper/lower/digit/symbol, Fisher–Yates shuffle. Returns a `string`. Reusable as-is for the reissued password.

### /home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Application/Common/Validation.cs
```csharp
public static void ValidatePassword(string password)
{
    if (password.Length < DomainConstants.PasswordMinLength)
        throw new ValidationAppException($"A senha deve ter ao menos {DomainConstants.PasswordMinLength} caracteres.");
    if (password.Length > DomainConstants.PasswordMaxLength)
        throw new ValidationAppException($"A senha deve ter no máximo {DomainConstants.PasswordMaxLength} caracteres.");
}
```
The generated password (length 14) always passes this, so no validation is needed on the reissued password.

### /home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Application/Abstractions/Identity.cs — IEmailSender (38-51)
```csharp
public interface IEmailSender
{
    Task SendPasswordResetAsync(string email, string resetToken, CancellationToken ct = default);
    /// <summary>Welcome email with the system-generated password and the deadline to change it.</summary>
    Task SendNewUserAsync(string email, string userName, string tempPassword, DateTimeOffset changeBy, CancellationToken ct = default);
    /// <summary>Reminder sent on login once the change deadline passed and the password is still the issued one.</summary>
    Task SendPasswordChangeReminderAsync(string email, string userName, DateTimeOffset wasDue, CancellationToken ct = default);
    /// <summary>Alerts the device admins that free disk space crossed the low threshold.</summary>
    Task SendDiskLowAsync(IEnumerable<string> adminEmails, double freePercent, double freeGB, double totalGB, CancellationToken ct = default);
}
```
Existing methods: `SendPasswordResetAsync(email, resetToken)`, `SendNewUserAsync(email, userName, tempPassword, changeBy)`, `SendPasswordChangeReminderAsync(email, userName, wasDue)`, `SendDiskLowAsync(adminEmails, freePercent, freeGB, totalGB)`.

### /home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Infrastructure/Email/SmtpEmailSender.cs
`SendNewUserAsync` body (the email we will reuse): subject `"Bem-vindo ao Reflow Oven — sua senha de acesso"`, body shows `tempPassword` and `"Por segurança, troque sua senha no programa até {changeBy:dd/MM/yyyy}."`. `SendPasswordChangeReminderAsync` subject `"Lembrete: troque sua senha — Reflow Oven"`, body: `"O prazo para trocar sua senha venceu em {wasDue:dd/MM/yyyy}. Acesse o programa e defina uma nova senha o quanto antes."` (this one carries NO password). Private `SendAsync(to, subject, htmlBody, ct)` does the MailKit send.

### /home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Infrastructure/Email/StubEmailSender.cs
Logs each call. `SendNewUserAsync` logs `senha '{Password}' (trocar até {ChangeBy:dd/MM/yyyy})`. If a new interface method is added, both senders must implement it.

### /home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Infrastructure/Auth/JwtTokenService.cs
```csharp
public const string MustChangeClaim = "must_change_password";
public TokenResult CreateForUser(User user) =>
    Create(user.Id.ToString(), user.Name, user.Type.ToString(), calibration: false, mustChangePassword: user.MustChangePassword);
```
`must_change_password` claim is only added when `user.MustChangePassword`. On the hard-expiry path NO token is minted (login is rejected), so this claim is unaffected for that path; the `me` endpoint reads it back (`AuthController.Me`, line 38).

### /home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Application/Common/Defaults.cs
```csharp
public const string DefaultDevPassword = "reflow1234";
```
Used only by the seeder. **Seeded users get `MustChangePassword=false` and `PasswordIssuedAt=null`** (DbSeeder, lines 36-45, sets neither) — so seeded users are NOT subject to any change cycle and will NOT be affected by the hard-expiry path. Only users created via `UserService.CreateAsync` (which sets `MustChangePassword=true` + `PasswordIssuedAt=now`) hit the cycle.

### Controllers / frontend (contract context)
- `/home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Api/Controllers/UsersController.cs`: `POST /api/users` is `AdminOnly`, calls `users.CreateAsync(req, ct)`, returns 201 `CreatedAtAction` with `UserDto`.
- `/home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Api/Controllers/AuthController.cs`: `Login` returns `LoginResult` (always HTTP 200), `ChangePassword` -> `auth.ChangePasswordAsync`.
- Frontend `/home/lsilva/ProjetosLucas/reflow-oven-front/src/lib/auth.ts` (login, 16-28): on `!result.ok` it returns `{ ok:false, error: result.error ?? "Falha no login." }` and surfaces `result.error` directly. So a new pt-BR `error` string on a `LoginResult.Fail` is displayed verbatim with no frontend change.
- Frontend `/home/lsilva/ProjetosLucas/reflow-oven-front/src/lib/api.ts`: `SessionDto`/`LoginResult` types do NOT include `mustChangePassword` or `passwordExpired`; `Session` type (auth.ts:10) also omits them. The backend already sends `MustChangePassword` on the session but the front ignores it.

## Change plan

1. **AuthService.LoginAsync — replace the soft reminder block with the HARD-expiry path.** File `AuthService.cs`, method `LoginAsync`, the block currently at lines 54-79 (from `var now = clock.UtcNow;` through the `if (remind) {...}`). Keep `user.LastLogin`/`user.LoginCount` updates only for the success path. New logic, inserted AFTER the password `Verify` succeeds (after line 52) and BEFORE minting the token:
   - Compute `var now = clock.UtcNow;`
   - Detect expiry: `if (user.MustChangePassword && user.PasswordIssuedAt is { } issued && now >= issued.AddDays(DomainConstants.PasswordChangeWithinDays))` → this is the expired-temp-password branch. (The user passed `hasher.Verify`, so they typed the correct-but-expired temp password.)
   - Inside that branch:
     - Generate a new temp: `var newTemp = PasswordGenerator.Generate();`
     - `user.PasswordHash = hasher.Hash(newTemp);`
     - `user.PasswordIssuedAt = now;` (resets the deadline anchor)
     - `user.MustChangePassword = true;` (stays true — defensive)
     - Throttle the reissue email reusing `LastPasswordReminderAt`: `var throttled = user.LastPasswordReminderAt is { } last && last > now.AddDays(-1);` Only send if `!throttled`; on send set `user.LastPasswordReminderAt = now;`. **Important caveat:** if you ALWAYS reissue+rehash on every expired login but only email when not throttled, a user whose email is throttled gets a new password they were never told — they are locked out for up to a day. To avoid that, gate the WHOLE reissue (rehash + new `PasswordIssuedAt` + email) on `!throttled`; when throttled, do NOT rehash, just reject. (See open questions — pick one.)
     - `var changeBy = now.AddDays(DomainConstants.PasswordChangeWithinDays);`
     - Email it (best-effort, reuse `SendNewUserAsync`): `try { await email.SendNewUserAsync(user.Email, user.Name, newTemp, changeBy, ct); } catch (Exception ex) { logger.LogError(...); }`
     - `await db.SaveChangesAsync(ct);` (persist new hash + timestamps)
     - `logger.LogWarning("Senha temporária de '{User}' expirada; nova senha gerada e enviada.", user.Name);`
     - `return LoginResult.Fail("Sua senha provisória expirou. Enviamos uma nova senha para o seu e-mail.");`  ← **new pt-BR contract string (proposed exact literal).**
   - For the non-expired path (or `MustChangePassword==false`): set `user.LastLogin = now; user.LoginCount++; await db.SaveChangesAsync(ct);` then mint the token and return `LoginResult.Success(...)` exactly as today (keep the existing `SessionDto` construction with `user.MustChangePassword ? true : null`).
   - Delete the now-unused `remind`/`due`/reminder block and the `SendPasswordChangeReminderAsync` call.

2. **AuthService.ChangePasswordAsync — reset the deadline anchors on self-service change.** File `AuthService.cs`, method `ChangePasswordAsync`, the block at lines 102-104. Add after `user.PasswordChangedAt = clock.UtcNow;`:
   - `user.PasswordIssuedAt = null;` (no longer on a system-issued password)
   - `user.LastPasswordReminderAt = null;` (clear throttle)
   This prevents a stale `PasswordIssuedAt` from ever re-triggering expiry once the user owns their password. `MustChangePassword=false` already gates it, but clearing is correct and cheap.

3. **UserService.CreateAsync — formalize MODEL B (stop accepting req.Password).** File `UserService.cs`, method `CreateAsync` (21-65). The body already ignores `req.Password` and does generate+email+`MustChangePassword=true`+`PasswordIssuedAt=now`, so **no behavioral change is required**. The only change is the contract: remove `Password` from `CreateUserRequest` (step 5) so the request stops carrying a dead/ignored field; this method needs no edit beyond removing nothing (it never reads `req.Password`). Optionally update the comment at lines 32-33 to say the deadline is hard (login is rejected after expiry). The deadline-start timestamp is `PasswordIssuedAt = now` (already set, line 48) — that is the anchor the hard-expiry login reads.

4. **(Optional, only if a distinct reissue email is wanted) Add IEmailSender.SendPasswordReissuedAsync.** File `Identity.cs` interface (38-51): add `Task SendPasswordReissuedAsync(string email, string userName, string newTempPassword, DateTimeOffset changeBy, CancellationToken ct = default);`. Implement in `SmtpEmailSender.cs` (new subject e.g. `"Sua senha provisória expirou — nova senha de acesso"`, body showing `newTempPassword` + `changeBy`) and in `StubEmailSender.cs` (a log line). **Recommendation: skip this and reuse `SendNewUserAsync`** — its body ("Use a senha abaixo... troque sua senha até {changeBy}") fits the reissue case exactly, requires zero new methods, and both senders already implement it. Do NOT reuse `SendPasswordChangeReminderAsync` — it deliberately carries no password.

5. **UserDtos.cs — drop Password from CreateUserRequest (MODEL B).** File `UserDtos.cs`, lines 19-24. Change `public sealed record CreateUserRequest(string Name, string Email, string Password, UserType Type = UserType.Regular, UserStatus Status = UserStatus.Ativo);` → `public sealed record CreateUserRequest(string Name, string Email, UserType Type = UserType.Regular, UserStatus Status = UserStatus.Ativo);`. (Frontend must stop sending `password` — TODO item 2 already documents this front change at `users.ts`/`api.ts`/`UserCreateModal.tsx`.) `UpdateUserRequest.Password` stays (admin reset is a separate, intentional feature). No change to `UserDto` needed for the login-expiry path.

6. **AuthDtos.cs — decide passwordExpired exposure.** Recommendation: **rely on the `error` string only**, no DTO change. The frontend `login()` already surfaces `result.error` verbatim, so the new literal from step 1 reaches the user with zero frontend type changes. If a machine-readable flag is preferred instead, add `bool? PasswordExpired = null` to `LoginResult` (it is a plain record, additive, non-breaking) and set it on the reject branch; the frontend `LoginResult` interface in `api.ts` would then need the optional field added. Prefer the error-string approach to keep the contract minimal.

7. **No JwtTokenService change.** The reject branch mints no token, so the `must_change_password` claim path is untouched. Leave `JwtTokenService.cs` as-is.

## Migration?
**no.** Every field the hard-expiry path needs already exists on `User` and is already mapped/migrated: `MustChangePassword` (bool), `PasswordIssuedAt`, `PasswordChangedAt`, `LastPasswordReminderAt` (all `DateTimeOffset?`). The plan only changes runtime logic and (in MODEL B) removes a property from the request DTO — neither touches the schema. There is no enum/CHECK constraint on these columns that would block anything (the only CHECK constraints in this codebase are the `Id = 1` singletons and the username POSIX check, unrelated to `User` password fields). A migration is needed ONLY if you choose step 6's optional `PasswordExpired` flag on a persisted entity (you would not — it lives on a DTO, not an entity) or add a new persisted column; the recommended plan adds neither.

## Contract/enum notes
- **New pt-BR `error` literal (becomes part of the `{ok,error}` login contract):** proposed exact string `"Sua senha provisória expirou. Enviamos uma nova senha para o seu e-mail."`. It is returned via `LoginResult.Fail(...)` on HTTP 200 (same shape as the existing failures) and shown verbatim by the frontend `login()`.
- **Existing login error literals that MUST stay byte-exact** (frontend/tests depend on them): `"Informe o usuário."`, `"Usuário não encontrado."`, `"Usuário inativo."`, `"Senha incorreta."`, and (change-password) `"Senha atual incorreta."`, `"Sessão sem usuário (login técnico não troca senha)."`, `"Usuário não encontrado."`.
- **Conflict literal** in CreateAsync: `"Já existe um usuário com esse nome."` (unchanged).
- **No `[JsonStringEnumMemberName]` enums are touched** by this change. `UserType` (Admin/Regular), `UserStatus` (Ativo/Inativo) wire literals stay exact and are unaffected.
- **`SessionDto` shape stays the same** (the recommended plan adds nothing to it; `MustChangePassword` already present and already ignored by the front). If step 6's optional flag is taken, `LoginResult` gains an additive nullable bool — backward compatible.
- **MODEL B removes `CreateUserRequest.Password`** — this is a breaking change to the request body the frontend currently sends (`api.ts createUser`, `users.ts upsertUser`); coordinate with the front change already scoped in TODO item 2. The backend already ignores the field, so removing it changes no backend behavior, only the wire contract.

## Risks / open questions
- **Throttle-vs-lockout race (the core design decision):** if you rehash on EVERY expired login but only email when not throttled, a user can be issued a password they never receive (locked out up to 1 day). Safer: gate the entire reissue (rehash + reset `PasswordIssuedAt` + email) behind `!throttled`; when throttled, reject WITHOUT rehashing (their last-emailed temp still works until they use it, but it is expired so they still can't get in — they must wait/use the already-sent new one). Decide: (a) always reissue + best-effort email, or (b) reissue only when not throttled. Recommend (b).
- **Reissued temp could itself expire before use** if the user ignores the email for `PasswordChangeWithinDays` days — next login generates yet another. That is the intended loop, but combined with throttling it means a user who never reads email is permanently locked out. Acceptable for the stated design; flag for product sign-off.
- **Best-effort email + persisted new hash:** if `SaveChangesAsync` succeeds but the email send fails, the old temp is dead and the new temp is only in logs (stub) or lost (smtp). Mitigate by sending BEFORE save, or by only rotating the hash when the send succeeds. Current `CreateAsync` saves first then emails best-effort; mirroring that here risks lockout. Recommend: send first (or treat send failure as "do not rotate").
- **`changeBy` formatting:** `SendNewUserAsync` formats `changeBy` as `dd/MM/yyyy` (date only). Reusing it for reissue is fine; the body wording "Use a senha abaixo no primeiro acesso" is slightly off for a reissue but acceptable. A dedicated method (step 4) would read better.
- **Seeded users are exempt** (`MustChangePassword=false`, `PasswordIssuedAt=null`), so the hard expiry only ever affects admin-created users — confirm that is intended (the seeded admin used for factory-reset will never be force-expired).
- **`ChangePasswordAsync` currently leaves `PasswordIssuedAt` set** even after a self-change; without step 2's clear, a future toggling of `MustChangePassword` back to true (none today) could mis-fire expiry. Low risk but step 2 is the clean fix.
- **MODEL B front coordination:** removing `CreateUserRequest.Password` will 400/deserialize-ignore the front's current `password` field; the front must drop it (TODO item 2 already plans this). If the front is not updated in lockstep, extra `password` JSON is simply ignored by System.Text.Json (no error by default), so it is forward-tolerant — but the front still wrongly collects/validates a password it no longer needs.
- **No automated test coverage was located** for the login reminder path in this investigation; adding/updating a test for the new reject literal and the rotation is advisable but outside the read scope.