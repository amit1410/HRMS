using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Common;

/// <summary>
/// Turns an ordered query into a single page of results.
/// </summary>
public static class QueryablePagingExtensions
{
    /// <summary>
    /// Counts the matching rows and returns the requested page.
    /// <para>
    /// Page and page size are clamped rather than trusted. Validators already reject out-of-range values on
    /// the HTTP path, but this is the last line before <c>Skip</c>/<c>Take</c>: a negative skip throws and
    /// an unbounded take would return the whole table, so neither may depend on a caller having validated
    /// first.
    /// </para>
    /// </summary>
    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(
        this IQueryable<T> query,
        PagedQuery paging,
        CancellationToken cancellationToken = default)
    {
        var page = Math.Max(1, paging.Page);
        var pageSize = Math.Clamp(paging.PageSize, 1, PagedQuery.MaxPageSize);

        var totalCount = await query.CountAsync(cancellationToken);
        if (totalCount == 0)
        {
            return PagedResult<T>.Empty(page, pageSize);
        }

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<T>(items, page, pageSize, totalCount);
    }
}
