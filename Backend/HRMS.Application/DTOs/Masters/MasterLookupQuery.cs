using HRMS.Application.Common;

namespace HRMS.Application.DTOs.Masters;

/// <summary>
/// Query parameters for master data lookup endpoints.
/// </summary>
public class MasterLookupQuery
{
    /// <summary>Filter to only active records (default: true).</summary>
    public bool ActiveOnly { get; set; } = true;

    /// <summary>Optional search term to filter by Code or Name.</summary>
    public string? Search { get; set; }

    /// <summary>Optional parent ID for hierarchical masters (e.g. DepartmentId for SubDepartments).</summary>
    public Guid? ParentId { get; set; }
}
