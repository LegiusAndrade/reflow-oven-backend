namespace ReflowOven.Application.Services;

/// <summary>
/// Programs gallery + editor backend: search/filter/sort/paginate, CRUD (segments → profile via
/// <see cref="ProfileBuilder"/>), per-user favorites, soft-delete and change-log auditing.
/// </summary>
public sealed class ProgramService(IAppDbContext db, IClock clock, AuditService audit, ICurrentUser current)
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
        var size = Math.Clamp(q.PageSize, 1, DomainConstants.ProgramPageSizeMax);
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

        // Snapshot the previous curve, overwrite, then diff old-vs-new point by point so the change-log
        // carries the full antes×depois with a precise per-point role (added/changed/removed/unchanged).
        var before = BuildPoints(program, ChangePointRole.ChangedBefore);

        program.Name = name;
        program.Description = description;
        program.Segments = segments;
        program.Profile = profile;

        var after = BuildPoints(program, ChangePointRole.ChangedAfter);
        audit.RecordProgramChange(ChangeAction.Editado, program, BuildEditDiff(before, after));
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
        program.DeletedBy = current.Name;
        audit.RecordProgramChange(ChangeAction.Removido, program, BuildPoints(program, ChangePointRole.Removed));
        await audit.BumpActivityAsync(Defaults.ActivityLabels[6], ct); // programas deletados
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Master "trash": soft-deleted programs (newest first), with who/when. Seed programs included.</summary>
    public async Task<IReadOnlyList<DeletedProgramDto>> ListDeletedAsync(CancellationToken ct = default)
    {
        var rows = await db.Programs.IgnoreQueryFilters()
            .Where(p => p.IsDeleted)
            .OrderByDescending(p => p.DeletedAt)
            .ToListAsync(ct);
        return rows.Select(p => new DeletedProgramDto(p.Id, p.Name, p.DeletedAt, p.DeletedBy)).ToList();
    }

    /// <summary>Master action: bring a soft-deleted program back into the catalog.</summary>
    public async Task RestoreAsync(string id, CancellationToken ct = default)
    {
        var program = await db.Programs.IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == id && p.IsDeleted, ct)
            ?? throw new NotFoundException("Programa apagado não encontrado.");
        program.IsDeleted = false;
        program.DeletedAt = null;
        program.DeletedBy = null;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Master action: permanently delete a soft-deleted program (irreversible; its favorites cascade).</summary>
    public async Task PurgeAsync(string id, CancellationToken ct = default)
    {
        var program = await db.Programs.IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == id && p.IsDeleted, ct)
            ?? throw new NotFoundException("Programa apagado não encontrado.");
        db.Programs.Remove(program); // FK cascade drops its favorites
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Set or toggle the program's favorite flag for the user; returns the new state. When
    /// <paramref name="desired"/> is given it is an idempotent set (no-op if already in that state);
    /// when null it toggles the current state.
    /// </summary>
    public async Task<bool> ToggleFavoriteAsync(string id, Guid userId, bool? desired = null, CancellationToken ct = default)
    {
        if (!await db.Programs.AnyAsync(p => p.Id == id, ct))
            throw new NotFoundException("Programa não encontrado.");

        var fav = await db.Favorites.FirstOrDefaultAsync(f => f.UserId == userId && f.ProgramId == id, ct);
        var isFav = fav is not null;
        var target = desired ?? !isFav;

        if (target == isFav)
            return isFav; // idempotent no-op

        if (target)
            db.Favorites.Add(new FavoriteProgram { UserId = userId, ProgramId = id });
        else
            db.Favorites.Remove(fav!);

        await db.SaveChangesAsync(ct);
        return target;
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
            if (req.Profile.Count > DomainConstants.ProfileMaxPoints)
                throw new ValidationAppException($"Máximo de {DomainConstants.ProfileMaxPoints} pontos no perfil.");

            profile = req.Profile.Select(p =>
            {
                if (p.T < 0)
                    throw new ValidationAppException("Tempo do ponto não pode ser negativo.");
                // The t=0 baseline start (StartTemp) is exempt from the entry floor; every later point ≥ PointTempMin.
                var tempMin = p.T > 0 ? DomainConstants.PointTempMin : 0;
                if (p.Temp < tempMin || p.Temp > DomainConstants.PointTempMax)
                    throw new ValidationAppException($"Temperatura fora da faixa {DomainConstants.PointTempMin}..{DomainConstants.PointTempMax} °C.");
                return new ProfilePoint { T = p.T, Temp = p.Temp };
            }).ToList();
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

    /// <summary>
    /// Diff the old and new curves point-by-point (by position/index) into one role-tagged list:
    /// an unchanged point appears once (<see cref="ChangePointRole.Unchanged"/>); a changed point appears
    /// twice — <see cref="ChangePointRole.ChangedBefore"/> + <see cref="ChangePointRole.ChangedAfter"/> at
    /// the same index; a point only in the new curve is <see cref="ChangePointRole.Added"/>; one only in
    /// the old curve is <see cref="ChangePointRole.Removed"/>. The front rebuilds the <i>before</i> curve
    /// from removed+changed-before+unchanged and the <i>after</i> curve from added+changed-after+unchanged,
    /// and labels each point from its role. Position-based: inserting a point mid-curve shifts the rest, so
    /// the tail reads as changed — simple, predictable, and matches the index-keyed tables in the UI.
    /// </summary>
    private static List<ChangePointRow> BuildEditDiff(IReadOnlyList<ChangePointRow> before, IReadOnlyList<ChangePointRow> after)
    {
        var rows = new List<ChangePointRow>();
        var max = Math.Max(before.Count, after.Count);
        for (var i = 0; i < max; i++)
        {
            var b = i < before.Count ? before[i] : null;
            var a = i < after.Count ? after[i] : null;

            if (b is not null && a is not null)
            {
                if (b.Temp == a.Temp && b.TimeSec == a.TimeSec && b.Ramp == a.Ramp)
                {
                    a.Role = ChangePointRole.Unchanged;
                    rows.Add(a);
                }
                else
                {
                    b.Role = ChangePointRole.ChangedBefore;
                    a.Role = ChangePointRole.ChangedAfter;
                    rows.Add(b);
                    rows.Add(a);
                }
            }
            else if (a is not null)
            {
                a.Role = ChangePointRole.Added;
                rows.Add(a);
            }
            else
            {
                b!.Role = ChangePointRole.Removed;
                rows.Add(b);
            }
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
