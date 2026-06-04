namespace ReflowOven.Application.Services;

/// <summary>
/// Login + password recovery. Reproduces the frontend auth contract: the same pt-BR error
/// strings, the special technician login, and the {ok,error} login result shape.
/// </summary>
public sealed class AuthService(
    IAppDbContext db,
    IPasswordHasher hasher,
    IJwtTokenService jwt,
    IClock clock,
    ITechnicianCredentials technician,
    IEmailSender email,
    ICurrentUser current,
    AuditService audit,
    ILogger<AuthService> logger)
{
    public async Task<LoginResult> LoginAsync(LoginRequest req, CancellationToken ct = default)
    {
        var username = (req.Username ?? "").Trim();
        if (username.Length == 0)
        {
            logger.LogWarning("Login recusado: usuário não informado.");
            return LoginResult.Fail("Informe o usuário.");
        }
        var password = req.Password ?? "";

        // Hidden technician session — full Admin, no User row.
        if (string.Equals(username, technician.Username, StringComparison.OrdinalIgnoreCase) && technician.Verify(password))
        {
            logger.LogInformation("Login OK: técnico '{User}' (Calibração).", username);
            audit.Record(OperationType.Login, OperationObject.Sessao, "calibracao", [OperationField.Of("papel", "Técnico (Calibração)")], operatorName: "Técnico");
            await db.SaveChangesAsync(ct);
            var t = jwt.CreateForCalibration();
            var session = new SessionDto("calibration", "Calibração", UserType.Admin, t.IssuedAtUnixMs, true);
            return LoginResult.Success(t.Token, t.ExpiresAt, session);
        }

        var lower = username.ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Name.ToLower() == lower, ct);

        // SECURITY: never disclose which of the username/password was wrong. A distinct "user not found"
        // vs "wrong password" lets an attacker enumerate valid usernames, so both return the SAME generic
        // message. We also run one hash for a missing user so the two cases take the same time — otherwise
        // the response latency itself is an enumeration oracle (existing user = slow BCrypt verify).
        var passwordOk = user is not null && hasher.Verify(password, user.PasswordHash);
        if (user is null)
            _ = hasher.Hash(password); // burn an equivalent BCrypt cost; result discarded

        if (user is null || !passwordOk)
        {
            logger.LogWarning("Login falhou: credenciais inválidas para '{User}'.", username);
            audit.Record(OperationType.Login, OperationObject.Sessao, username, [OperationField.Of("resultado", "falha")], operatorName: username);
            await db.SaveChangesAsync(ct);
            return LoginResult.Fail("Usuário ou senha incorretos.");
        }

        // Reachable only once the password is correct, so surfacing an inactive account leaks no existence
        // to an attacker (they'd already need valid credentials) while still telling a real user why.
        if (user.Status == UserStatus.Inativo)
        {
            logger.LogWarning("Login falhou: usuário '{User}' está inativo.", user.Name);
            return LoginResult.Fail("Usuário inativo.");
        }

        var now = clock.UtcNow;

        // Hard password-change policy: if the user is still on the system-issued password and the deadline
        // has passed, the provisional password is rejected. We generate a fresh one, email it (best-effort,
        // throttled to once a day so a retry storm can't spam), and refuse the login until they use the new
        // password. The deadline is anchored on PasswordIssuedAt (set on create/reissue).
        if (user.MustChangePassword && user.PasswordIssuedAt is { } issued)
        {
            var deadline = issued.AddDays(DomainConstants.PasswordChangeWithinDays);
            if (now > deadline)
            {
                var throttled = user.LastPasswordReminderAt is { } last && last > now.AddDays(-1);
                if (!throttled)
                {
                    // Rotate the provisional password and email the new one (only when not throttled, so we
                    // never issue a password the user wasn't told about).
                    var newTemp = PasswordGenerator.Generate();
                    user.PasswordHash = hasher.Hash(newTemp);
                    user.PasswordIssuedAt = now;
                    user.MustChangePassword = true;
                    user.LastPasswordReminderAt = now;
                    var changeBy = now.AddDays(DomainConstants.PasswordChangeWithinDays);
                    await db.SaveChangesAsync(ct);

                    logger.LogWarning("Senha provisória de '{User}' expirada; nova senha gerada e enviada.", user.Name);
                    try { await email.SendNewUserAsync(user.Email, user.Name, newTemp, changeBy, ct); }
                    catch (Exception ex) { logger.LogError(ex, "Falha ao enviar a nova senha provisória para '{Email}'.", user.Email); }
                }
                else
                {
                    logger.LogWarning("Senha provisória de '{User}' expirada; reenvio ainda em janela de throttle.", user.Name);
                }

                return LoginResult.Fail("Sua senha provisória expirou. Enviamos uma nova senha para o seu e-mail.");
            }
        }

        user.LastLogin = now;
        user.LoginCount++;
        audit.Record(OperationType.Login, OperationObject.Sessao, user.Name, [OperationField.Of("papel", user.Type)], operatorId: user.Id, operatorName: user.Name);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Login OK: '{User}' ({Type}).", user.Name, user.Type);

        var tok = jwt.CreateForUser(user);
        var prefs = UserService.MapPrefs(user.Preferences);
        var dto = new SessionDto(user.Id.ToString(), user.Name, user.Type, tok.IssuedAtUnixMs, null,
            user.MustChangePassword ? true : null, prefs.Theme, prefs.ChartSeries);
        return LoginResult.Success(tok.Token, tok.ExpiresAt, dto);
    }

    /// <summary>Authenticated self-service password change; clears the forced-change flag.</summary>
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

        var now = clock.UtcNow;
        user.PasswordHash = hasher.Hash(req.NewPassword!);
        user.MustChangePassword = false;
        user.PasswordChangedAt = now;
        // The user now owns their password — clear the issued-password deadline anchor + reminder throttle.
        user.PasswordIssuedAt = null;
        user.LastPasswordReminderAt = null;
        audit.Record(OperationType.Alteracao, OperationObject.Usuario, user.Name, [OperationField.Of("senha", "alterada")], operatorId: user.Id, operatorName: user.Name);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Senha alterada por '{User}'.", user.Name);
        return new OkResponse();
    }

    /// <summary>Stateless logout (the client discards the JWT) — recorded on the operation log for the
    /// security/audit trail. The caller supplies the identity from the JWT claims.</summary>
    public async Task LogoutAsync(Guid? userId, string userName, CancellationToken ct = default)
    {
        audit.Record(OperationType.Logout, OperationObject.Sessao, userName, operatorId: userId, operatorName: userName);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Logout: '{User}'.", userName);
    }

    /// <summary>Enumeration-safe: always reports success even if the email is unknown.</summary>
    public async Task<OkResponse> ForgotPasswordAsync(ForgotPasswordRequest req, CancellationToken ct = default)
    {
        var addr = (req.Email ?? "").Trim().ToLowerInvariant();
        logger.LogInformation("Recuperação de senha solicitada para '{Email}'.", addr);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == addr, ct);
        if (user is not null)
        {
            var raw = Guid.NewGuid().ToString("N");
            db.PasswordResetTokens.Add(new PasswordResetToken
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                TokenHash = hasher.Hash(raw),
                ExpiresAt = clock.UtcNow.AddHours(1),
            });
            await db.SaveChangesAsync(ct);
            await email.SendPasswordResetAsync(user.Email, raw, ct);
        }
        return new OkResponse();
    }
}
