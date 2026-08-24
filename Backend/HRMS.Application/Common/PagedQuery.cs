namespace HRMS.Application.Common;

/// <summary>
/// Common paging, search and sort inputs for list endpoints, bound from the query string.
/// <para>
/// <see cref="SortBy"/> is deliberately validated against a per-endpoint whitelist rather than being fed
/// to a dynamic-LINQ expression: accepting an arbitrary property path would let a caller order by — and
/// so probe — columns the endpoint never meant to expose, and would be an injection surface in any
/// string-built ORDER BY. An unrecognized value is rejected instead of silently ignored, so a client
/// never believes it sorted when it did not.
/// </para>
/// </summary>
public abstract class PagedQuery
{
    public const int DefaultPageSize = 20;

    /// <summary>Upper bound on a single page, so one request cannot pull an entire dataset.</summary>
    public const int MaxPageSize = 100;

    /// <summary>1-based page number.</summary>
    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = DefaultPageSize;

    /// <summary>Free-text filter. Which fields it matches is defined per endpoint.</summary>
    public string? Search { get; set; }

    /// <summary>Field to order by. Must be one of the endpoint's supported fields.</summary>
    public string? SortBy { get; set; }

    public bool SortDescending { get; set; }
}
