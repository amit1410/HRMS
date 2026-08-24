using HRMS.Application.Common;

namespace HRMS.Application.DTOs.Departments;

/// <summary>Query-string filters for the department list.</summary>
public class DepartmentQuery : PagedQuery
{
    /// <summary>Restrict to active or retired departments. Null returns both.</summary>
    public bool? IsActive { get; set; }

    /// <summary>
    /// Fields the list may be ordered by. The service and the validator both read this list, so a field
    /// cannot be advertised without being implemented (a test asserts every entry actually sorts).
    /// </summary>
    public static readonly IReadOnlyList<string> SortFields =
    [
        "code", "name", "employeeCount", "isActive", "createdDate"
    ];
}
