using HRMS.Domain.Common;
using HRMS.Domain.Enums;

namespace HRMS.Domain.Entities;

/// <summary>
/// A family member of an employee. Multiple family members are supported per employee.
/// Every add/update/delete is auditable through the audit log.
/// </summary>
public class EmployeeFamily : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    public Guid EmployeeId { get; set; }

    public string? Salutation { get; set; }

    public string FirstName { get; set; } = string.Empty;

    public string? MiddleName { get; set; }

    public string LastName { get; set; } = string.Empty;

    /// <summary>Relationship to the employee, e.g. "Spouse", "Father", "Child".</summary>
    public string Relationship { get; set; } = string.Empty;

    public Gender Gender { get; set; } = Gender.Unspecified;

    public DateOnly? DateOfBirth { get; set; }

    public BloodGroup BloodGroup { get; set; } = BloodGroup.Unspecified;

    public string? Nationality { get; set; }

    public string? Occupation { get; set; }

    public bool IsNominee { get; set; }

    public bool IsDependent { get; set; }

    public decimal? NomineePercentage { get; set; }

    // Navigation
    public Tenant? Tenant { get; set; }
    public Employee? Employee { get; set; }
}
