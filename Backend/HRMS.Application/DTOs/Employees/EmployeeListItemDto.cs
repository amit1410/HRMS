using HRMS.Domain.Enums;

namespace HRMS.Application.DTOs.Employees;

/// <summary>
/// The subset of an employee record a directory listing needs. Deliberately narrower than
/// <c>EmployeeDto</c>: date of birth, address and phone number are personal data that has no business
/// being broadcast in a page of 100 rows just because it fits on the detail screen.
/// </summary>
public record EmployeeListItemDto(
    Guid Id,
    string EmployeeCode,
    string FullName,
    string Email,
    string? DepartmentName,
    string? DesignationName,
    EmployeeStatus Status,
    DateOnly DateOfJoining,
    bool IsCurrentlyEmployed);
