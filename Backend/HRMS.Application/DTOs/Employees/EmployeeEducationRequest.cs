using HRMS.Domain.Enums;

namespace HRMS.Application.DTOs.Employees;

public class EmployeeEducationRequest
{
    public string EducationLevel { get; set; } = string.Empty;
    public string Qualification { get; set; } = string.Empty;
    public string? University { get; set; }
    public string? Institute { get; set; }
    public EducationType EducationType { get; set; } = EducationType.FullTime;
    public string? AreaOfSpecialization { get; set; }
    public int? YearOfPassing { get; set; }
    public string? Score { get; set; }
    public string? DocumentOfProof { get; set; }
}
