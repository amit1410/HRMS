using HRMS.Domain.Common;

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

    /// <summary>Original hire date into the organization (may predate current employment).</summary>
    public DateOnly FirstHiredDate { get; set; }

    /// <summary>Date the employee joined the current employer.</summary>
    public DateOnly DateOfJoining { get; set; }

    /// <summary>Date the employee joined the parent group/company.</summary>
    public DateOnly? GroupDateOfJoining { get; set; }

    /// <summary>Date the employee was confirmed from probation.</summary>
    public DateOnly? ConfirmationDate { get; set; }

    /// <summary>Employment status within the current contract (e.g. "Probation", "Confirmed", "Contractual").</summary>
    public string? JobStatus { get; set; }

    /// <summary>Numeric probation period value (e.g. 6).</summary>
    public int? ProbationPeriod { get; set; }

    /// <summary>Unit for <see cref="ProbationPeriod"/>: "Days", "Months", or "Years".</summary>
    public string? ProbationPeriodUnit { get; set; }

    /// <summary>Employee who referred this person (if any).</summary>
    public Guid? ReferredByEmployeeId { get; set; }

    /// <summary>Numeric notice period value (e.g. 30).</summary>
    public int? NoticePeriod { get; set; }

    /// <summary>Unit for <see cref="NoticePeriod"/>: "Days" or "Months".</summary>
    public string? NoticePeriodUnit { get; set; }

    // Navigation
    public Tenant? Tenant { get; set; }
    public Employee? Employee { get; set; }
    public Employee? ReferredByEmployee { get; set; }
}
