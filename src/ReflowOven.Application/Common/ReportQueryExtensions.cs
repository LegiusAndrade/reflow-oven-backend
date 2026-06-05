namespace ReflowOven.Application.Common;

/// <summary>Shared paging / date-range helpers for the Relatórios + Log de Operação list queries (so the
/// clamp and the inclusive-'To' rule live in one place instead of being copied per service).</summary>
public static class ReportQueryExtensions
{
    /// <summary>Server-clamped (page, size): page ≥ 1 and size in 1..ReportPageSizeMax, so a client can
    /// never pull an unbounded result set.</summary>
    public static (int page, int size) Paging(this ReportQuery q) =>
        (Math.Max(1, q.Page), Math.Clamp(q.PageSize, 1, DomainConstants.ReportPageSizeMax));

    /// <summary>The inclusive 'To' date as an exclusive upper bound (the next midnight), so a yyyy-mm-dd
    /// value (which parses to 00:00) still includes rows from that whole day.</summary>
    public static DateTimeOffset? ToExclusive(this ReportQuery q) =>
        q.To is { } to ? new DateTimeOffset(to.Date.AddDays(1), to.Offset) : null;
}
