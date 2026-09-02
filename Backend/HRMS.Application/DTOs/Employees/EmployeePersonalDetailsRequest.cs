using HRMS.Domain.Enums;

namespace HRMS.Application.DTOs.Employees;

/// <summary>
/// Create/update payload for the Employee → <b>Personal Details</b> section only.
/// <para>
/// Deliberately stripped to personal and statutory information. It has no department, designation, reporting
/// manager, holding company, LOB, organisation, grade, work location or cost centre id — those belong to
/// later Employment/Position sections and must not be entered through this form — and no email/phone/address,
/// which are captured by their own sections. Employee code is not a client input here: the backend assigns
/// it according to the tenant's employee-code configuration (shown to the user as "New Hire" before save).
/// </para>
/// </summary>
public class EmployeePersonalDetailsRequest
{
    /// <summary>Salutation, e.g. "Mr.", "Mrs.", "Dr.", "Ms.".</summary>
    public string? Salutation { get; set; }

    public string FirstName { get; set; } = string.Empty;

    public string? MiddleName { get; set; }

    public string LastName { get; set; } = string.Empty;

    public DateOnly? DateOfBirth { get; set; }

    public Gender Gender { get; set; } = Gender.Unspecified;

    public BloodGroup BloodGroup { get; set; } = BloodGroup.Unspecified;

    public MaritalStatus MaritalStatus { get; set; } = MaritalStatus.Unspecified;

    public Guid? BirthCountryId { get; set; }

    public Guid? BirthStateId { get; set; }

    public Guid? BirthCityId { get; set; }

    public string? Religion { get; set; }

    public string? Caste { get; set; }

    /// <summary>Country of citizenship. Rendered as the country dropdown; stores the country name.</summary>
    public string? Citizenship { get; set; }

    public bool EsicApplicable { get; set; }

    public string? EsicNumber { get; set; }

    public string? PfNumber { get; set; }

    public string? MediclaimNumber { get; set; }

    public string? UanNumber { get; set; }

    public bool Gratuity { get; set; }

    public bool Pension { get; set; }

    public string? AadhaarNumber { get; set; }

    public string? PanNumber { get; set; }

    public DateOnly DateOfJoining { get; set; }

    public string? JobStatus { get; set; }
}
