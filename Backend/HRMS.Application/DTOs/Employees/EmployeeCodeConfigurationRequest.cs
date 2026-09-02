namespace HRMS.Application.DTOs.Employees;
using HRMS.Domain.Enums;

public sealed class EmployeeCodeConfigurationRequest
{
    public bool AutoGenerate { get; set; } = true;
    public EmployeeCodeAssignmentMode AssignmentMode { get; set; } = EmployeeCodeAssignmentMode.Auto;
    public EmployeeCodeGenerationMethod? GenerationMethod { get; set; } = EmployeeCodeGenerationMethod.Simple;
    public string Prefix { get; set; } = "EMP";
    public long NextNumber { get; set; } = 1;
    public int Padding { get; set; }
    public string Separator { get; set; } = "-";
    public DateOnly EffectiveFrom { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);
    public DateOnly? EffectiveTo { get; set; }
}
