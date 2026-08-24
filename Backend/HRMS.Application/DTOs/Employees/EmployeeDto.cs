using HRMS.Domain.Enums;

namespace HRMS.Application.DTOs.Employees;

/// <summary>
/// A full employee record. Department, designation and manager are returned as id plus display name so a
/// client can render the record without three follow-up requests, while still having the ids it needs to
/// pre-select values in an edit form.
/// </summary>
public record EmployeeDto(
    Guid Id,
    string EmployeeCode,
    string FirstName,
    string LastName,
    string FullName,
    string Email,
    string? Phone,
    DateOnly? DateOfBirth,
    Gender Gender,
    DateOnly DateOfJoining,
    DateOnly? DateOfLeaving,
    EmployeeStatus Status,
    Guid DepartmentId,
    string DepartmentName,
    Guid DesignationId,
    string DesignationName,
    Guid? ReportingManagerId,
    string? ReportingManagerName,
    string? Address,
    DateTime CreatedDate,
    DateTime? ModifiedDate);
