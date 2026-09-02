namespace HRMS.Application.DTOs.Employees;

/// <summary>
/// Represents a potential supervisor option returned by the supervisor-options endpoint.
/// Used to populate searchable dropdowns filtered by supervisor type eligibility.
/// </summary>
public record SupervisorOptionDto(
    Guid EmployeeId,
    string EmployeeCode,
    string FullName,
    string? DepartmentName,
    string? DesignationName);
