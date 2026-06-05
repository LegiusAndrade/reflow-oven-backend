namespace ReflowOven.Application.Services;

/// <summary>
/// Read-only backend for the Log de Operação — the universal audit trail. Paged and filtered like the other
/// Relatórios lists (search/from/to/before/page/pageSize), plus the operation-specific filters operator/type/
/// object/objectId. Append-only and protected from the Manutenção cleanup; rows are written by
/// <see cref="AuditService"/> at every service seam.
/// </summary>
public sealed class OperationLogService(IAppDbContext db)
{
    public async Task<PagedResult<OperationLogEntryDto>> ListAsync(ReportQuery q, CancellationToken ct = default)
    {
        IQueryable<OperationLogEntry> query = db.OperationLog.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var s = q.Search.Trim().ToLower();
            query = query.Where(x => x.OperatorName.ToLower().Contains(s) || (x.ObjectId != null && x.ObjectId.ToLower().Contains(s)));
        }
        if (q.From is not null) query = query.Where(x => x.At >= q.From);
        if (q.ToExclusive() is { } toExc) query = query.Where(x => x.At < toExc);
        if (q.Before is not null) query = query.Where(x => x.At < q.Before);
        if (!string.IsNullOrWhiteSpace(q.Operator))
        {
            var op = q.Operator.Trim().ToLower();
            query = query.Where(x => x.OperatorName.ToLower().Contains(op));
        }
        if (EnumWire.TryFromWire<OperationCategory>(q.Category, out var cat)) query = query.Where(x => x.Category == cat);
        if (EnumWire.TryFromWire<OperationType>(q.Type, out var type)) query = query.Where(x => x.Type == type);
        if (EnumWire.TryFromWire<OperationObject>(q.Object, out var obj)) query = query.Where(x => x.Object == obj);
        if (!string.IsNullOrWhiteSpace(q.ObjectId)) query = query.Where(x => x.ObjectId == q.ObjectId);

        var total = await query.CountAsync(ct);
        var (page, size) = q.Paging();
        // Materialize then map: Data is an owned jsonb collection, projected in memory (≤ ReportPageSizeMax rows).
        var rows = await query
            .OrderByDescending(x => x.At).ThenByDescending(x => x.Id)
            .Skip((page - 1) * size).Take(size)
            .ToListAsync(ct);
        var items = rows.Select(Map).ToList();
        return new PagedResult<OperationLogEntryDto>(items, total, page, size);
    }

    private static OperationLogEntryDto Map(OperationLogEntry x) => new(
        x.Id, x.At, x.OperatorName, x.OperatorId, x.Category, x.Type, x.Object, x.ObjectId,
        x.Data.Select(d => new OperationFieldDto(d.Field, d.Before, d.After)).ToList());
}
