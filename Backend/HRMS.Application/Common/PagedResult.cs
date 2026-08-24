namespace HRMS.Application.Common;

/// <summary>
/// One page of results plus the totals a client needs to render pagination. Returned instead of a bare
/// list so a caller can never accidentally fetch an entire tenant's employees in one request.
/// </summary>
public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public bool HasPreviousPage => Page > 1;

    public bool HasNextPage => Page < TotalPages;

    public static PagedResult<T> Empty(int page, int pageSize) => new([], page, pageSize, 0);
}
