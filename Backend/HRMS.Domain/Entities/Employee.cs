using HRMS.Domain.Common;
using HRMS.Domain.Enums;

namespace HRMS.Domain.Entities;

/// <summary>
/// A person employed by a tenant. Distinct from <see cref="User"/> on purpose: a user is a login, an
/// employee is an HR record. Most employees never sign in, and a login (an external auditor, a support
/// account) need not correspond to an employee — linking the two is a later concern.
/// <para>
/// Compensation is deliberately absent: it belongs with payroll, where it needs its own permission and a
/// change history. Storing a bare current-salary column here would expose it to everyone holding
/// <c>Employee.View</c> and lose every change ever made to it.
/// </para>
/// </summary>
public class Employee : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    /// <summary>The organization's own identifier for this employee, e.g. "EMP-001". Unique within the tenant.</summary>
    public string EmployeeCode { get; set; } = string.Empty;

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    /// <summary>Work email address. Unique per tenant, and unrelated to any <see cref="User"/> login.</summary>
    public string Email { get; set; } = string.Empty;

    public string? Phone { get; set; }

    public DateOnly? DateOfBirth { get; set; }

    public Gender Gender { get; set; } = Gender.Unspecified;

    public DateOnly DateOfJoining { get; set; }

    /// <summary>Last working day. Set once the employee is no longer <see cref="EmployeeStatus.Active"/>.</summary>
    public DateOnly? DateOfLeaving { get; set; }

    public EmployeeStatus Status { get; set; } = EmployeeStatus.Active;

    public Guid DepartmentId { get; set; }

    public Guid DesignationId { get; set; }

    /// <summary>
    /// The employee this person reports to, within the same tenant. Optional — the top of the reporting
    /// line reports to nobody.
    /// </summary>
    public Guid? ReportingManagerId { get; set; }

    public string? Address { get; set; }

    // Navigation
    public Tenant? Tenant { get; set; }
    public Department? Department { get; set; }
    public Designation? Designation { get; set; }
    public Employee? ReportingManager { get; set; }
    public ICollection<Employee> DirectReports { get; set; } = new List<Employee>();
}
