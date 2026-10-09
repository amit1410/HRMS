using HRMS.Domain.Common;
using HRMS.Domain.Enums;

namespace HRMS.Domain.Entities;

/// <summary>
/// Core employment details for an employee — joining information and contractual terms.
/// This is the employee's "employment file" (1:1 with Employee) and is separate from the
/// effective-dated position history.
/// </summary>
public class EmployeeEmployment : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    public Guid EmployeeId { get; set; }

    // Date of joining is owned by the employee's personal details (Employee.DateOfJoining),
    // which is the single source of truth. It is intentionally not duplicated here.

    /// <summary>Date the employee was confirmed from probation.</summary>
    public DateOnly? ConfirmationDate { get; set; }

    /// <summary>Employment status within the current contract (e.g. "Probation", "Confirmed", "Contractual").</summary>
    public string? JobStatus { get; set; }

    /// <summary>Employee who referred this person (if any).</summary>
    public Guid? ReferredByEmployeeId { get; set; }

    /// <summary>Numeric notice period value (e.g. 30). Consumed by the separation approval/settlement workflow.</summary>
    public int? NoticePeriod { get; set; }

    /// <summary>Unit for <see cref="NoticePeriod"/>: "Days" or "Months".</summary>
    public string? NoticePeriodUnit { get; set; }

    public DateOnly? NoticeStartDate { get; set; }
    public DateOnly? NoticeEndDate { get; set; }
    public NoticePeriodStatus NoticeStatus { get; set; } = NoticePeriodStatus.NotServing;

    // Navigation
    public Tenant? Tenant { get; set; }
    public Employee? Employee { get; set; }
    public Employee? ReferredByEmployee { get; set; }
}
