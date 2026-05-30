namespace ReflowOven.Application.Common;

/// <summary>A page of results plus the total count, for the paginated report/list endpoints.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);
