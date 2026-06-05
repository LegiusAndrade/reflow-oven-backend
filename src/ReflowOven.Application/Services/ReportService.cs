namespace ReflowOven.Application.Services;

/// <summary>Read-only Relatórios backend: Execuções, Erros, Alterações, fault catalog and system log.</summary>
public sealed class ReportService(IAppDbContext db)
{
    // --- executions -----------------------------------------------------------------------
    public async Task<PagedResult<ExecutionSummaryDto>> ExecutionsAsync(ReportQuery q, CancellationToken ct = default)
    {
        IQueryable<ExecutionReport> query = db.Executions;
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var s = q.Search.Trim().ToLower();
            query = query.Where(e => e.ProgramName.ToLower().Contains(s) || (e.UserName != null && e.UserName.ToLower().Contains(s)));
        }
        if (q.From is not null) query = query.Where(e => e.StartedAt >= q.From);
        if (q.ToExclusive() is { } toExc) query = query.Where(e => e.StartedAt < toExc);
        if (EnumWire.TryFromWire<ExecutionStatus>(q.Status, out var status)) query = query.Where(e => e.Status == status);

        var total = await query.CountAsync(ct);
        var (page, size) = q.Paging();
        var items = await query
            .OrderByDescending(e => e.StartedAt)
            .Skip((page - 1) * size).Take(size)
            .Select(e => new ExecutionSummaryDto(e.Id, e.ProgramId, e.ProgramName, e.UserName, e.StartedAt, e.DurationSeconds, e.Status, e.PeakTemp, e.PeakCurrent))
            .ToListAsync(ct);
        return new PagedResult<ExecutionSummaryDto>(items, total, page, size);
    }

    public async Task<ExecutionDetailDto> ExecutionAsync(Guid id, CancellationToken ct = default)
    {
        var e = await db.Executions.Include(x => x.Events).FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException("Execução não encontrada.");
        return new ExecutionDetailDto(
            e.Id, e.ProgramId, e.ProgramName, e.UserId, e.UserName, e.StartedAt, e.DurationSeconds, e.Status,
            e.PeakTemp, e.PeakCurrent, e.FaultAtT, e.FaultAtTemp,
            e.Points.Select(p => new ExecProfilePointDto(p.T, p.Temp, p.Kind)).ToList(),
            e.Comparison.Select(c => new ProfileComparisonRowDto(c.TempProg, c.TempReal, c.TimeProgSeconds, c.TimeRealSeconds, c.StageIndex)).ToList(),
            e.Events.OrderBy(v => v.OrderIndex).ThenBy(v => v.At).Select(MapEvent).ToList(),
            MapSnapshot(e.Trace),
            e.FailureReason, e.FaultTypeCode, e.LinkedErrorId);
    }

    // --- errors ---------------------------------------------------------------------------
    public async Task<PagedResult<ErrorSummaryDto>> ErrorsAsync(ReportQuery q, CancellationToken ct = default)
    {
        IQueryable<ErrorLogEntry> query = db.Errors;
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var s = q.Search.Trim().ToLower();
            query = query.Where(e => e.Message.ToLower().Contains(s) || e.FaultTypeCode.ToLower().Contains(s));
        }
        if (q.From is not null) query = query.Where(e => e.At >= q.From);
        if (q.ToExclusive() is { } toExc) query = query.Where(e => e.At < toExc);
        if (EnumWire.TryFromWire<ErrorSeverity>(q.Severity, out var severity)) query = query.Where(e => e.Severity == severity);

        var total = await query.CountAsync(ct);
        var (page, size) = q.Paging();
        var items = await query
            .OrderByDescending(e => e.At)
            .Skip((page - 1) * size).Take(size)
            .Select(e => new ErrorSummaryDto(e.Id, e.At, e.FaultTypeCode, e.Severity, e.Message, e.UserName, e.ProgramName))
            .ToListAsync(ct);
        return new PagedResult<ErrorSummaryDto>(items, total, page, size);
    }

    public async Task<ErrorDetailDto> ErrorAsync(Guid id, CancellationToken ct = default)
    {
        var e = await db.Errors.Include(x => x.Events).FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException("Falha não encontrada.");
        var snap = MapSnapshot(e.Snapshot);
        return new ErrorDetailDto(
            e.Id, e.At, e.FaultTypeCode, e.Severity, e.Message, e.UserId, e.UserName, e.ProgramId, e.ProgramName,
            e.OvenTemp, e.PcbTemp, e.StartAt, e.EndAt, e.InputVoltage, e.OutputVoltage, snap,
            e.Events.OrderBy(v => v.OrderIndex).ThenBy(v => v.At).Select(MapEvent).ToList());
    }

    // --- changes --------------------------------------------------------------------------
    public async Task<PagedResult<ChangeSummaryDto>> ChangesAsync(ReportQuery q, CancellationToken ct = default)
    {
        IQueryable<ChangeLogEntry> query = db.Changes;
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var s = q.Search.Trim().ToLower();
            query = query.Where(c => c.Target.ToLower().Contains(s) || (c.UserName != null && c.UserName.ToLower().Contains(s)));
        }
        if (q.From is not null) query = query.Where(c => c.At >= q.From);
        if (q.ToExclusive() is { } toExc) query = query.Where(c => c.At < toExc);
        if (q.Before is not null) query = query.Where(c => c.At < q.Before);
        if (EnumWire.TryFromWire<ChangeAction>(q.Action, out var action)) query = query.Where(c => c.Action == action);
        if (!string.IsNullOrWhiteSpace(q.ProgramId)) query = query.Where(c => c.ProgramId == q.ProgramId);

        var total = await query.CountAsync(ct);
        var (page, size) = q.Paging();
        var items = await query
            .OrderByDescending(c => c.At)
            .Skip((page - 1) * size).Take(size)
            .Select(c => new ChangeSummaryDto(c.Id, c.At, c.Action, c.Target, c.UserName, c.DetailKind))
            .ToListAsync(ct);
        return new PagedResult<ChangeSummaryDto>(items, total, page, size);
    }

    public async Task<ChangeDetailDto> ChangeAsync(Guid id, CancellationToken ct = default)
    {
        var c = await db.Changes.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException("Alteração não encontrada.");
        var rows = c.Points.OrderBy(p => p.Index)
            .Select(p => new ChangePointRowDto(p.Index, p.Temp, p.TimeSec, p.Ramp, p.Role)).ToList();

        // Consolidate the stored role-rows (a changed point is two rows) into one diff per index, plus the
        // before/after curves the editor charts. Config changes have no points → no diff/curves.
        ChangeDiffDto? diff = null;
        IReadOnlyList<ProfilePointDto>? beforeCurve = null;
        IReadOnlyList<ProfilePointDto>? afterCurve = null;
        if (rows.Count > 0)
            (diff, beforeCurve, afterCurve) = BuildChangeDiff(rows);

        return new ChangeDetailDto(
            c.Id, c.At, c.Action, c.Target, c.UserName, c.ProgramId, c.DetailKind,
            c.ConfigBullets, rows, diff, beforeCurve, afterCurve);
    }

    /// <summary>
    /// Collapse the persisted <see cref="ChangePointRole"/> rows into a consolidated per-index diff:
    /// a changed point's before+after pair becomes one "changed" row with the differing fields; added/
    /// removed/unchanged map straight through. Derives the before curve (rows with a before side:
    /// removed + changed-before + unchanged) and the after curve (added + changed-after + unchanged),
    /// ordered by index.
    /// </summary>
    private static (ChangeDiffDto Diff, List<ProfilePointDto> BeforeCurve, List<ProfilePointDto> AfterCurve)
        BuildChangeDiff(IReadOnlyList<ChangePointRowDto> rows)
    {
        var points = new List<ChangePointDiffDto>();
        foreach (var g in rows.GroupBy(r => r.Index).OrderBy(g => g.Key))
        {
            var bRow = g.FirstOrDefault(r => r.Role is ChangePointRole.ChangedBefore or ChangePointRole.Removed or ChangePointRole.Unchanged);
            var aRow = g.FirstOrDefault(r => r.Role is ChangePointRole.ChangedAfter or ChangePointRole.Added or ChangePointRole.Unchanged);

            var before = bRow is null ? null : new ChangePointValueDto(bRow.Temp, bRow.TimeSec, bRow.Ramp);
            var after = aRow is null ? null : new ChangePointValueDto(aRow.Temp, aRow.TimeSec, aRow.Ramp);

            string status;
            if (before is not null && after is not null)
                status = g.Any(r => r.Role is ChangePointRole.ChangedBefore or ChangePointRole.ChangedAfter) ? "changed" : "unchanged";
            else if (after is not null) status = "added";
            else status = "removed";

            var fields = new List<string>();
            if (status == "changed")
            {
                if (before!.Temp != after!.Temp) fields.Add("temp");
                if (before.TimeSec != after.TimeSec) fields.Add("timeSec");
                if (before.Ramp != after.Ramp) fields.Add("ramp");
            }

            points.Add(new ChangePointDiffDto(g.Key, status, before, after, fields));
        }

        var changedFieldCounts = points
            .SelectMany(p => p.ChangedFields)
            .GroupBy(f => f)
            .ToDictionary(g => g.Key, g => g.Count());

        var summary = new ChangeDiffSummaryDto(
            TotalChanges: points.Count(p => p.Status != "unchanged"),
            Added: points.Count(p => p.Status == "added"),
            Removed: points.Count(p => p.Status == "removed"),
            Changed: points.Count(p => p.Status == "changed"),
            Unchanged: points.Count(p => p.Status == "unchanged"),
            ChangedFields: changedFieldCounts);

        var beforeCurve = ExpandCurve(points.Where(p => p.Before is not null).Select(p => p.Before!));
        var afterCurve = ExpandCurve(points.Where(p => p.After is not null).Select(p => p.After!));

        return (new ChangeDiffDto(summary, points), beforeCurve, afterCurve);
    }

    /// <summary>
    /// Rebuild the real setpoint curve from the snapshot vertices so the change chart matches what the
    /// program actually runs. The stored rows are the per-leg vertices (cumulative time + target + ramp);
    /// turning them back into editable segments and re-expanding through <see cref="ProfileBuilder.ToProfile"/>
    /// restores the t=0 baseline, the parabola sub-points and a <c>Fixo</c> leg's held temperature — none of
    /// which survive a plain "vertex → point" projection (that dropped the baseline, straightened parabolas
    /// and plotted a Fixo's raw, unused temperature). Point-defined programs round-trip unchanged: their legs
    /// are linear, so the expansion reproduces the same straight segments.
    /// </summary>
    private static List<ProfilePointDto> ExpandCurve(IEnumerable<ChangePointValueDto> vertices)
    {
        var verts = vertices.ToList(); // already in index order (cumulative time strictly increasing)
        if (verts.Count == 0) return [];

        var segments = new List<ProfileSegment>(verts.Count);
        var prevT = 0;
        foreach (var v in verts)
        {
            segments.Add(new ProfileSegment { Temp = (int)Math.Round(v.Temp), DurationSec = Math.Max(0, v.TimeSec - prevT), Ramp = v.Ramp });
            prevT = v.TimeSec;
        }
        return ProfileBuilder.ToProfile(segments).Select(p => new ProfilePointDto(p.T, p.Temp)).ToList();
    }

    // --- fault catalog & system log -------------------------------------------------------
    public async Task<IReadOnlyList<FaultTypeDto>> FaultTypesAsync(CancellationToken ct = default) =>
        await db.FaultTypes.OrderBy(f => f.Code).Select(f => new FaultTypeDto(f.Code, f.Severity, f.Message)).ToListAsync(ct);

    public async Task<PagedResult<SystemLogDto>> SystemLogAsync(ReportQuery q, CancellationToken ct = default)
    {
        IQueryable<SystemLogEntry> query = db.SystemLog;
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var s = q.Search.Trim().ToLower();
            query = query.Where(l => l.Message.ToLower().Contains(s));
        }
        if (q.From is not null) query = query.Where(l => l.At >= q.From);
        if (q.ToExclusive() is { } toExc) query = query.Where(l => l.At < toExc);
        if (EnumWire.TryFromWire<LogLevel>(q.Level, out var level)) query = query.Where(l => l.Level == level);

        var total = await query.CountAsync(ct);
        var (page, size) = q.Paging();
        var items = await query
            .OrderByDescending(l => l.Id)
            .Skip((page - 1) * size).Take(size)
            .Select(l => new SystemLogDto(l.Id, l.At, l.Level, l.Message))
            .ToListAsync(ct);
        return new PagedResult<SystemLogDto>(items, total, page, size);
    }

    private static LogEventDto MapEvent(LogEvent v) => new(v.At, v.Kind, v.Message);

    private static FailureSnapshotDto MapSnapshot(FailureSnapshot s) =>
        new(s.DurationSec, (s.Series ?? []).Select(sr => new SnapshotSeriesDto(sr.Name, sr.Unit, sr.Color, sr.Values)).ToList());
}
