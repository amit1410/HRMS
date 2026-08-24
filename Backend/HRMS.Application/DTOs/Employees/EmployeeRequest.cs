using HRMS.Domain.Enums;

namespace HRMS.Application.DTOs.Employees;

/// <summary>
/// Create/update payload for an employee.
/// <para>
/// The three ids are the sensitive part: each is resolved through the caller's own tenant before it is
/// used, so an id belonging to another organization is indistinguishable from one that does not exist. No
/// TenantId is accepted — it comes from the authenticated token.
/// </para>
/// </summary>
public class EmployeeRequest
{
    /// <summary>The organization's own identifier for the employee. Unique within the tenant.</summary>
    public string EmployeeCode { get; set; } = string.Empty;

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string? Phone { get; set; }

    public DateOnly? DateOfBirth { get; set; }

    public Gender Gender { get; set; } = Gender.Unspecified;

    public DateOnly DateOfJoining { get; set; }

    /// <summary>Required once <see cref="Status"/> is anything other than Active, and forbidden while it is.</summary>
    public DateOnly? DateOfLeaving { get; set; }

    public EmployeeStatus Status { get; set; } = EmployeeStatus.Active;

    public Guid DepartmentId { get; set; }

    public Guid DesignationId { get; set; }

    /// <summary>Another employee of the same tenant, or null for someone at the top of the reporting line.</summary>
    public Guid? ReportingManagerId { get; set; }

    public string? Address { get; set; }
}
