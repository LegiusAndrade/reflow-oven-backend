namespace ReflowOven.Application.Services;

/// <summary>
/// Programs gallery + editor backend: search/filter/sort/paginate, CRUD (segments → profile via
/// <see cref="ProfileBuilder"/>), per-user favorites, soft-delete and change-log auditing.
/// </summary>
public sealed class ProgramService(IAppDbContext db, IClock clock, AuditService audit)
{
    public async Task<PagedResult<ProgramDto>> ListAsync(ProgramListQuery q, Guid? userId, CancellationToken ct = default)
    {
        var favIds = userId is null
            ? []
            : await db.Favorites.Where(f => f.UserId == userId).Select(f => f.ProgramId).ToListAsync(ct);
        var favSet = favIds.ToHashSet();

        IQueryable<ReflowProgram> query = db.Programs;

        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var s = q.Search.Trim().ToLower();
            query = query.Where(p => p.Name.ToLower().Contains(s));
        }

        query = q.Filter switch
        {
            ProgramFilter.Favorites => query.Where(p => favIds.Contains(p.Id)),
            ProgramFilter.Unused => query.Where(p => p.RunCount == 0),
            ProgramFilter.Used => query.Where(p => p.RunCount > 0),
            _ => query,
        };

        // peakTemp/totalTime are derived from the jsonb profile, so order in memory.
        var all = await query.ToListAsync(ct);
        IEnumerable<ReflowProgram> sorted = q.Sort switch
        {
            ProgramSort.Recent => all.OrderByDescending(p => p.LastUsed ?? DateTimeOffset.MinValue),
            ProgramSort.MostUsed => all.OrderByDescending(p => p.RunCount),
            ProgramSort.Name => all.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase),
            ProgramSort.Temp => all.OrderByDescending(Peak),
            ProgramSort.Duration => all.OrderByDescending(Total),
            _ => all.OrderBy(p => p.IsSeed)
                    .ThenByDescending(p => p.LastUsed ?? DateTimeOffset.MinValue)
                    .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase),
        };

        var list = sorted.ToList();
        var page = Math.Max(1, q.Page);
        var size = Math.Clamp(q.PageSize, 1, 100);
        var items = list.Skip((page - 1) * size).Take(size).Select(p => Map(p, favSet.Contains(p.Id))).ToList();
        return new PagedResult<ProgramDto>(items, list.Count, page, size);
    }

    public async Task<ProgramDto> GetAsync(string id, Guid? userId, CancellationToken ct = default)
    {
        var p = await db.Programs.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException("Programa não encontrado.");
        var fav = userId is not null && await db.Favorites.AnyAsync(f => f.UserId == userId && f.ProgramId == id, ct);
        return Map(p, fav);
    }

    public async Task<ProgramDto> CreateAsync(SaveProgramRequest req, CancellationToken ct = default)
    {
        var (name, description, segments, profile) = Validate(req);
        var program = new ReflowProgram
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            Description = description,
            RunCount = 0,
            LastUsed = null,
            IsSeed = false,
            Segments = segments,
            Profile = profile,
        };
        db.Programs.Add(program);
        audit.RecordProgramChange(ChangeAction.Criado, program, BuildPoints(program, ChangePointRole.Added));
        await audit.BumpActivityAsync(Defaults.ActivityLabels[4], ct); // programas criados
        await db.SaveChangesAsync(ct);
        return Map(program, false);
    }

    public async Task<ProgramDto> UpdateAsync(string id, SaveProgramRequest req, Guid? userId, CancellationToken ct = default)
    {
        var program = await db.Programs.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException("Programa não encontrado.");

        var (name, description, segments, profile) = Validate(req);
        program.Name = name;
        program.Description = description;
        program.Segments = segments;
        program.Profile = profile;

        audit.RecordProgramChange(ChangeAction.Editado, program, BuildPoints(program, ChangePointRole.ChangedAfter));
        await audit.BumpActivityAsync(Defaults.ActivityLabels[5], ct); // programas alterados
        await db.SaveChangesAsync(ct);

        var fav = userId is not null && await db.Favorites.AnyAsync(f => f.UserId == userId && f.ProgramId == id, ct);
        return Map(program, fav);
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        var program = await db.Programs.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException("Programa não encontrado.");

        program.IsDeleted = true;
        program.DeletedAt = clock.UtcNow;
        audit.RecordProgramChange(ChangeAction.Removido, program, BuildPoints(program, ChangePointRole.Removed));
        await audit.BumpActivityAsync(Defaults.ActivityLabels[6], ct); // programas deletados
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Toggle the program's favorite flag for the user; returns the new state.</summary>
    public async Task<bool> ToggleFavoriteAsync(string id, Guid userId, CancellationToken ct = default)
    {
        if (!await db.Programs.AnyAsync(p => p.Id == id, ct))
            throw new NotFoundException("Programa não encontrado.");

        var fav = await db.Favorites.FirstOrDefaultAsync(f => f.UserId == userId && f.ProgramId == id, ct);
        if (fav is null)
        {
            db.Favorites.Add(new FavoriteProgram { UserId = userId, ProgramId = id });
            await db.SaveChangesAsync(ct);
            return true;
        }

        db.Favorites.Remove(fav);
        await db.SaveChangesAsync(ct);
        return false;
    }

    private static (string name, string? description, List<ProfileSegment>? segments, List<ProfilePoint> profile) Validate(SaveProgramRequest req)
    {
        var name = (req.Name ?? "").Trim();
        if (name.Length == 0)
            throw new ValidationAppException("Informe o nome do programa.");
        if (name.Length > DomainConstants.ProgramNameMaxLength)
            throw new ValidationAppException($"Nome excede {DomainConstants.ProgramNameMaxLength} caracteres.");

        var description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim();
        if (description is { Length: > DomainConstants.ProgramDescriptionMaxLength })
            throw new ValidationAppException($"Descrição excede {DomainConstants.ProgramDescriptionMaxLength} caracteres.");

        List<ProfileSegment>? segments = null;
        List<ProfilePoint> profile;

        if (req.Segments is { Count: > 0 })
        {
            if (req.Segments.Count > DomainConstants.ProfileMaxPoints)
                throw new ValidationAppException($"Máximo de {DomainConstants.ProfileMaxPoints} segmentos.");

            segments = req.Segments.Select(s =>
            {
                if (s.Temp < DomainConstants.PointTempMin || s.Temp > DomainConstants.PointTempMax)
                    throw new ValidationAppException($"Temperatura fora da faixa {DomainConstants.PointTempMin}..{DomainConstants.PointTempMax} °C.");
                if (s.DurationSec < DomainConstants.PointDurationMin || s.DurationSec > DomainConstants.PointDurationMax)
                    throw new ValidationAppException($"Duração fora da faixa {DomainConstants.PointDurationMin}..{DomainConstants.PointDurationMax} s.");
                return new ProfileSegment { Temp = s.Temp, DurationSec = s.DurationSec, Ramp = s.Ramp };
            }).ToList();
            profile = ProfileBuilder.ToProfile(segments);
        }
        else if (req.Profile is { Count: > 0 })
        {
            profile = req.Profile.Select(p => new ProfilePoint { T = p.T, Temp = p.Temp }).ToList();
        }
        else
        {
            throw new ValidationAppException("Informe ao menos um segmento ou ponto do perfil.");
        }

        return (name, description, segments, profile);
    }

    private static List<ChangePointRow> BuildPoints(ReflowProgram p, ChangePointRole role)
    {
        var rows = new List<ChangePointRow>();
        if (p.Segments is { Count: > 0 })
        {
            var i = 1;
            var t = 0;
            foreach (var s in p.Segments.Take(DomainConstants.ProfileMaxPoints))
            {
                t += s.DurationSec;
                rows.Add(new ChangePointRow { Index = i++, Temp = s.Temp, TimeSec = t, Ramp = s.Ramp, Role = role });
            }
        }
        else
        {
            var i = 1;
            foreach (var pt in p.Profile.Take(DomainConstants.ProfileMaxPoints))
                rows.Add(new ChangePointRow
                {
                    Index = i++,
                    Temp = (int)Math.Round(pt.Temp),
                    TimeSec = (int)Math.Round(pt.T),
                    Ramp = RampShape.Linear,
                    Role = role,
                });
        }
        return rows;
    }

    private static double Peak(ReflowProgram p) => p.Profile.Count > 0 ? p.Profile.Max(pt => pt.Temp) : 0;
    private static double Total(ReflowProgram p) => ProfileBuilder.TotalTime(p.Profile);

    private static ProgramDto Map(ReflowProgram p, bool favorite) => new(
        p.Id,
        p.Name,
        p.Description,
        p.RunCount,
        p.LastUsed,
        p.Profile.Select(pt => new ProfilePointDto(pt.T, pt.Temp)).ToList(),
        p.Segments?.Select(s => new ProfileSegmentDto(s.Temp, s.DurationSec, s.Ramp)).ToList(),
        favorite);
}
