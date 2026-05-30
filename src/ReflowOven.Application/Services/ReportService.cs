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
        if (q.To is not null) query = query.Where(e => e.StartedAt <= q.To);

        var total = await query.CountAsync(ct);
        var (page, size) = Paging(q);
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
            e.Events.OrderBy(v => v.OrderIndex).ThenBy(v => v.At).Select(MapEvent).ToList());
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
        if (q.To is not null) query = query.Where(e => e.At <= q.To);

        var total = await query.CountAsync(ct);
        var (page, size) = Paging(q);
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
        var snap = new FailureSnapshotDto(
            e.Snapshot.DurationSec,
            e.Snapshot.Series.Select(sr => new SnapshotSeriesDto(sr.Name, sr.Unit, sr.Color, sr.Values)).ToList());
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
        if (q.To is not null) query = query.Where(c => c.At <= q.To);

        var total = await query.CountAsync(ct);
        var (page, size) = Paging(q);
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
        return new ChangeDetailDto(
            c.Id, c.At, c.Action, c.Target, c.UserName, c.ProgramId, c.DetailKind,
            c.ConfigBullets,
            c.Points.OrderBy(p => p.Index).Select(p => new ChangePointRowDto(p.Index, p.Temp, p.TimeSec, p.Ramp, p.Role)).ToList());
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
        if (q.To is not null) query = query.Where(l => l.At <= q.To);

        var total = await query.CountAsync(ct);
        var (page, size) = Paging(q);
        var items = await query
            .OrderByDescending(l => l.Id)
            .Skip((page - 1) * size).Take(size)
            .Select(l => new SystemLogDto(l.Id, l.At, l.Level, l.Message))
            .ToListAsync(ct);
        return new PagedResult<SystemLogDto>(items, total, page, size);
    }

    private static (int page, int size) Paging(ReportQuery q) => (Math.Max(1, q.Page), Math.Clamp(q.PageSize, 1, 200));

    private static LogEventDto MapEvent(LogEvent v) => new(v.At, v.Kind, v.Message);
}
