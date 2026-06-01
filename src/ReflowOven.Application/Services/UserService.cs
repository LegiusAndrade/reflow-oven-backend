namespace ReflowOven.Application.Services;

/// <summary>CRUD for users (Usuários tab). Name is unique (case-insensitive) and immutable after create.</summary>
public sealed class UserService(
    IAppDbContext db, IPasswordHasher hasher, IClock clock, AuditService audit,
    ICurrentUser current, IEmailSender emailSender, ILogger<UserService> logger)
{
    public async Task<IReadOnlyList<UserDto>> ListAsync(CancellationToken ct = default)
    {
        // The dev Master is hidden from the Usuários grid (the front's UserDto.type is only Admin|Regular).
        var users = await db.Users.Include(u => u.ActivityStats)
            .Where(u => u.Type != UserType.Master)
            .OrderBy(u => u.Name).ToListAsync(ct);
        return users.Select(Map).ToList();
    }

    public async Task<UserDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var user = await db.Users.Include(u => u.ActivityStats).FirstOrDefaultAsync(u => u.Id == id, ct);
        // The dev Master is invisible to the Usuários screen — treat it as not found here too (matches ListAsync),
        // so its name/email/Type=Master never reach a UserDto (whose front contract is only Admin|Regular).
        if (user is null || user.Type == UserType.Master)
            throw new NotFoundException("Usuário não encontrado.");
        return Map(user);
    }

    public async Task<UserDto> CreateAsync(CreateUserRequest req, CancellationToken ct = default)
    {
        var name = (req.Name ?? "").Trim();
        Validation.ValidateUserName(name);
        var email = (req.Email ?? "").Trim();
        Validation.ValidateEmail(email);

        // The dev Master is seeded, not creatable via the API — block self-promotion via "type":"Master".
        if (req.Type == UserType.Master)
            throw new ValidationAppException("Tipo de usuário inválido.");

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

    public async Task<UserDto> UpdateAsync(Guid id, UpdateUserRequest req, CancellationToken ct = default)
    {
        var user = await db.Users.Include(u => u.ActivityStats).FirstOrDefaultAsync(u => u.Id == id, ct)
            ?? throw new NotFoundException("Usuário não encontrado.");

        // The dev Master is immutable through this screen, and no one may be promoted to Master here.
        if (user.Type == UserType.Master || req.Type == UserType.Master)
            throw new ForbiddenAppException("O usuário Master não pode ser alterado por esta tela.");

        var email = (req.Email ?? "").Trim();
        Validation.ValidateEmail(email);

        var willBeActiveAdmin = req.Type == UserType.Admin && req.Status == UserStatus.Ativo;
        if (!willBeActiveAdmin && await IsLastActiveAdminAsync(user.Id, ct))
            throw new ConflictException("Deve haver ao menos um administrador ativo.");

        user.Email = email;
        user.Type = req.Type;
        user.Status = req.Status;
        if (!string.IsNullOrEmpty(req.Password))
        {
            Validation.ValidatePassword(req.Password);
            user.PasswordHash = hasher.Hash(req.Password);
            // An admin deliberately set a known password — clear the forced-change cycle.
            user.MustChangePassword = false;
            user.PasswordChangedAt = clock.UtcNow;
        }

        await audit.BumpActivityAsync(Defaults.ActivityLabels[2], ct); // usuários alterados
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Usuário atualizado: '{Name}' ({Type}/{Status}) por '{Actor}'.", user.Name, user.Type, user.Status, current.Name);
        return Map(user);
    }

    /// <summary>The user's personal UI preferences (theme + chart series).</summary>
    public async Task<UserPreferencesDto> GetPreferencesAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new NotFoundException("Usuário não encontrado.");
        return MapPrefs(user.Preferences);
    }

    public async Task<UserPreferencesDto> UpdatePreferencesAsync(Guid userId, UserPreferencesDto dto, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new NotFoundException("Usuário não encontrado.");
        var p = user.Preferences;
        var s = dto.ChartSeries;
        p.Theme = dto.Theme;
        p.Alvo = s.Alvo; p.Oven = s.Oven; p.Board = s.Board; p.Current = s.Current;
        p.Voltage = s.Voltage; p.OvenFan = s.OvenFan; p.BoardFan = s.BoardFan;
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Preferências atualizadas: '{Name}' (tema {Theme}).", user.Name, p.Theme);
        return MapPrefs(p);
    }

    /// <summary>Maps the owned preferences to the DTO (chart series reuses <see cref="RunSeriesDto"/>).</summary>
    internal static UserPreferencesDto MapPrefs(UserPreferences p) =>
        new(p.Theme, new RunSeriesDto(p.Alvo, p.Oven, p.Board, p.Current, p.Voltage, p.OvenFan, p.BoardFan));

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct)
            ?? throw new NotFoundException("Usuário não encontrado.");

        // The dev Master is permanent — never removable through the Usuários screen.
        if (user.Type == UserType.Master)
            throw new ForbiddenAppException("O usuário Master não pode ser removido.");

        if (await IsLastActiveAdminAsync(id, ct))
            throw new ConflictException("Não é possível remover o único administrador ativo.");

        db.Users.Remove(user);
        await audit.BumpActivityAsync(Defaults.ActivityLabels[3], ct); // usuários deletados
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Usuário removido: '{Name}' por '{Actor}'.", user.Name, current.Name);
    }

    /// <summary>True when <paramref name="userId"/> is an active admin and no other active admin exists.</summary>
    private async Task<bool> IsLastActiveAdminAsync(Guid userId, CancellationToken ct)
    {
        var self = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (self is null || self.Type != UserType.Admin || self.Status != UserStatus.Ativo) return false;
        return !await db.Users.AnyAsync(u => u.Id != userId && u.Type == UserType.Admin && u.Status == UserStatus.Ativo, ct);
    }

    private static UserDto Map(User u) => new(
        u.Id.ToString(),
        u.Name,
        u.Email,
        u.Type,
        u.Status,
        u.CreatedAt,
        u.LastLogin,
        u.ActivityStats.OrderBy(s => s.Id).Select(s => new UserEventDto(s.Label, s.Count)).ToList());
}
