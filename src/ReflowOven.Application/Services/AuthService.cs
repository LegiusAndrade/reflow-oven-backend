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
            var t = jwt.CreateForCalibration();
            var session = new SessionDto("calibration", "Calibração", UserType.Admin, t.IssuedAtUnixMs, true);
            return LoginResult.Success(t.Token, t.ExpiresAt, session);
        }

        var lower = username.ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Name.ToLower() == lower, ct);
        if (user is null)
        {
            logger.LogWarning("Login falhou: usuário '{User}' não encontrado.", username);
            return LoginResult.Fail("Usuário não encontrado.");
        }
        if (user.Status == UserStatus.Inativo)
        {
            logger.LogWarning("Login falhou: usuário '{User}' está inativo.", user.Name);
            return LoginResult.Fail("Usuário inativo.");
        }
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

        user.PasswordHash = hasher.Hash(req.NewPassword!);
        user.MustChangePassword = false;
        user.PasswordChangedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Senha alterada por '{User}'.", user.Name);
        return new OkResponse();
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
