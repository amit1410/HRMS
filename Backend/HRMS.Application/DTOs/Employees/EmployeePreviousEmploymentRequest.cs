using HRMS.Domain.Enums;

namespace HRMS.Application.DTOs.Employees;

public class EmployeePreviousEmploymentRequest
{
    public string Company { get; set; } = string.Empty;
    public string? Designation { get; set; }
    public string? Location { get; set; }
    public EmploymentType EmploymentType { get; set; } = EmploymentType.FullTime;
    public DateOnly? TenureFrom { get; set; }
    public DateOnly? TenureTill { get; set; }
    public string? DocumentOfProof { get; set; }
}
