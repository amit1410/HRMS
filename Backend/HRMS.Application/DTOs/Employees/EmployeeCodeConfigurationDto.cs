namespace HRMS.Application.DTOs.Employees;
using HRMS.Domain.Enums;

public sealed record EmployeeCodeConfigurationDto(
    Guid Id, bool AutoGenerate, EmployeeCodeAssignmentMode AssignmentMode, EmployeeCodeGenerationMethod? GenerationMethod, string Prefix, long NextNumber, int Padding, string Separator, DateOnly EffectiveFrom, DateOnly? EffectiveTo);
