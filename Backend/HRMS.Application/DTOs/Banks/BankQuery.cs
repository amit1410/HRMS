using HRMS.Application.Common;

namespace HRMS.Application.DTOs.Banks;

/// <summary>Query-string filters for the bank list.</summary>
public class BankQuery : PagedQuery
{
    /// <summary>Restrict to active or retired banks. Null returns both.</summary>
    public bool? IsActive { get; set; }

    /// <summary>
    /// Fields the list may be ordered by. The service and the validator both read this list, so a field
    /// cannot be advertised without being implemented.
    /// </summary>
    public static readonly IReadOnlyList<string> SortFields =
    [
        "code", "name", "shortName", "bankType", "country", "isActive", "createdDate"
    ];
}
