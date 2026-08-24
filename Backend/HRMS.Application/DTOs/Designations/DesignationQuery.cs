using HRMS.Application.Common;

namespace HRMS.Application.DTOs.Designations;

/// <summary>Query-string filters for the designation list.</summary>
public class DesignationQuery : PagedQuery
{
    public bool? IsActive { get; set; }

    /// <summary>Fields the list may be ordered by. Anything else is a validation error, not a silent default.</summary>
    public static readonly IReadOnlyList<string> SortFields =
    [
        "code", "name", "employeeCount", "isActive", "createdDate"
    ];
}
