using HRMS.Domain.Enums;

namespace HRMS.Application.DTOs.Employees;

public record EmployeePreviousEmploymentDto(
    Guid Id,
    Guid EmployeeId,
    string Company,
    string? Designation,
    string? Location,
    EmploymentType EmploymentType,
    DateOnly? TenureFrom,
    DateOnly? TenureTill,
    string? DocumentOfProof,
    DateTime CreatedDate,
    DateTime? ModifiedDate);
