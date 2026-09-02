using HRMS.Domain.Enums;

namespace HRMS.Application.DTOs.Employees;

public class EmployeeFamilyRequest
{
    public string? Salutation { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string? MiddleName { get; set; }
    public string LastName { get; set; } = string.Empty;
    public string Relationship { get; set; } = string.Empty;
    public Gender Gender { get; set; } = Gender.Unspecified;
    public DateOnly? DateOfBirth { get; set; }
    public BloodGroup BloodGroup { get; set; } = BloodGroup.Unspecified;
    public string? Nationality { get; set; }
    public string? Occupation { get; set; }
    public bool IsNominee { get; set; }
    public bool IsDependent { get; set; }
    public decimal? NomineePercentage { get; set; }
}
