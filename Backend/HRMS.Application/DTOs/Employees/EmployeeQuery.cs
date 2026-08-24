using HRMS.Application.Common;
using HRMS.Domain.Enums;

namespace HRMS.Application.DTOs.Employees;

/// <summary>
/// Query-string filters for the employee list. <see cref="PagedQuery.Search"/> matches employee code,
/// first/last name and email.
/// </summary>
public class EmployeeQuery : PagedQuery
{
    public Guid? DepartmentId { get; set; }

    public Guid? DesignationId { get; set; }

    public EmployeeStatus? Status { get; set; }

    /// <summary>Restrict to the direct reports of one manager.</summary>
    public Guid? ReportingManagerId { get; set; }

    /// <summary>Fields the list may be ordered by. Anything else is a validation error, not a silent default.</summary>
    public static readonly IReadOnlyList<string> SortFields =
    [
        "employeeCode", "firstName", "lastName", "email", "department", "designation",
        "status", "dateOfJoining", "createdDate"
    ];
}
