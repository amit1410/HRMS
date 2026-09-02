using HRMS.Domain.Enums;

namespace HRMS.Application.DTOs.Employees;

public record EmployeeEducationDto(
    Guid Id,
    Guid EmployeeId,
    string EducationLevel,
    string Qualification,
    string? University,
    string? Institute,
    EducationType EducationType,
    string? AreaOfSpecialization,
    int? YearOfPassing,
    string? Score,
    string? DocumentOfProof,
    DateTime CreatedDate,
    DateTime? ModifiedDate);
