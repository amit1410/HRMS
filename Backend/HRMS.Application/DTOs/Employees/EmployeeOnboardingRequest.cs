namespace HRMS.Application.DTOs.Employees;

/// <summary>All data required to create an employee and their initial employment snapshot atomically.</summary>
public sealed class EmployeeOnboardingRequest
{
    public EmployeePersonalDetailsRequest PersonalDetails { get; set; } = new();
    public EmployeeContactRequest Contact { get; set; } = new();
    public EmploymentChangeRequest InitialEmployment { get; set; } = new();
}
