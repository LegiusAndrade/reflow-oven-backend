namespace ReflowOven.Application.Services;

/// <summary>CRUD for users (Usuários tab). Name is unique (case-insensitive) and immutable after create.</summary>
public sealed class UserService(IAppDbContext db, IPasswordHasher hasher, IClock clock, AuditService audit)
{
    public async Task<IReadOnlyList<UserDto>> ListAsync(CancellationToken ct = default)
    {
        var users = await db.Users.Include(u => u.ActivityStats).OrderBy(u => u.Name).ToListAsync(ct);
        return users.Select(Map).ToList();
    }

    public async Task<UserDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var user = await db.Users.Include(u => u.ActivityStats).FirstOrDefaultAsync(u => u.Id == id, ct)
            ?? throw new NotFoundException("Usuário não encontrado.");
        return Map(user);
    }

    public async Task<UserDto> CreateAsync(CreateUserRequest req, CancellationToken ct = default)
    {
        var name = (req.Name ?? "").Trim();
        Validation.ValidateUserName(name);
        var email = (req.Email ?? "").Trim();
        Validation.ValidateEmail(email);
        if (string.IsNullOrEmpty(req.Password))
            throw new ValidationAppException("Informe uma senha.");
        Validation.ValidatePassword(req.Password);

        var lower = name.ToLowerInvariant();
        if (await db.Users.AnyAsync(u => u.Name.ToLower() == lower, ct))
            throw new ConflictException("Já existe um usuário com esse nome.");

        var user = new User
        {
            Id = Guid.NewGuid(),
            Name = name,
            Email = email,
            PasswordHash = hasher.Hash(req.Password),
            Type = req.Type,
            Status = req.Status,
            CreatedAt = clock.UtcNow,
        };
        db.Users.Add(user);
        await audit.BumpActivityAsync(Defaults.ActivityLabels[1], ct); // usuários criados
        await db.SaveChangesAsync(ct);
        return Map(user);
    }

    public async Task<UserDto> UpdateAsync(Guid id, UpdateUserRequest req, CancellationToken ct = default)
    {
        var user = await db.Users.Include(u => u.ActivityStats).FirstOrDefaultAsync(u => u.Id == id, ct)
            ?? throw new NotFoundException("Usuário não encontrado.");

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
        }

        await audit.BumpActivityAsync(Defaults.ActivityLabels[2], ct); // usuários alterados
        await db.SaveChangesAsync(ct);
        return Map(user);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct)
            ?? throw new NotFoundException("Usuário não encontrado.");

        if (await IsLastActiveAdminAsync(id, ct))
            throw new ConflictException("Não é possível remover o único administrador ativo.");

        db.Users.Remove(user);
        await audit.BumpActivityAsync(Defaults.ActivityLabels[3], ct); // usuários deletados
        await db.SaveChangesAsync(ct);
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
