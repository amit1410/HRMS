namespace HRMS.Application.DTOs.Employees;

/// <summary>
/// Raw statutory identifiers for an authorized edit screen. General employee reads never return these values.
/// </summary>
public record EmployeeSensitiveDetailsDto(
    Guid EmployeeId,
    string? AadhaarNumber,
    string? PanNumber,
    string? UanNumber,
    string? PfNumber,
    string? EsicNumber,
    string? MediclaimNumber);
