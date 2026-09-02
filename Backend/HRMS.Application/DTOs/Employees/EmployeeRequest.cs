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

    public string? MiddleName { get; set; }

    public string LastName { get; set; } = string.Empty;

    /// <summary>Salutation, e.g. "Mr.", "Mrs.", "Dr.", "Ms.".</summary>
    public string? Salutation { get; set; }

    public string Email { get; set; } = string.Empty;

    public string? Phone { get; set; }

    public DateOnly? DateOfBirth { get; set; }

    public Gender Gender { get; set; } = Gender.Unspecified;

    public BloodGroup BloodGroup { get; set; } = BloodGroup.Unspecified;

    public MaritalStatus MaritalStatus { get; set; } = MaritalStatus.Unspecified;

    public string? BirthCountry { get; set; }

    public string? BirthState { get; set; }

    public string? BirthCity { get; set; }

    public Guid? BirthCountryId { get; set; }

    public Guid? BirthStateId { get; set; }

    public Guid? BirthCityId { get; set; }

    public string? Religion { get; set; }

    public string? Caste { get; set; }

    public string? EmployeeType { get; set; }

    public DateOnly DateOfJoining { get; set; }

    /// <summary>Group date of joining — the date the employee joined the parent group/company.</summary>
    public DateOnly? GroupDateOfJoining { get; set; }

    /// <summary>Required once <see cref="Status"/> is anything other than Active, and forbidden while it is.</summary>
    public DateOnly? DateOfLeaving { get; set; }

    public EmployeeStatus Status { get; set; } = EmployeeStatus.Active;

    public string? JobStatus { get; set; }

    public string? GroupId { get; set; }

    public Guid? DepartmentId { get; set; }

    public Guid? DesignationId { get; set; }

    /// <summary>Another employee of the same tenant, or null for someone at the top of the reporting line.</summary>
    public Guid? ReportingManagerId { get; set; }

    public string? AadhaarNumber { get; set; }

    public string? PanNumber { get; set; }

    public string? PfNumber { get; set; }

    public string? UanNumber { get; set; }

    public string? EsicNumber { get; set; }

    public string? MediclaimNumber { get; set; }

    public bool Gratuity { get; set; }

    public bool Pension { get; set; }

    public string? CostCenterCode { get; set; }

    public string? PayrollLocation { get; set; }

    public bool EsicApplicable { get; set; }

    public string? Citizenship { get; set; }

    public string? LanguageKnown { get; set; }

    public string? ProfilePictureUrl { get; set; }

    public string? Address { get; set; }
}
