using HRMS.Domain.Enums;

namespace HRMS.Application.DTOs.Employees;

public record EmployeeFamilyDto(
    Guid Id,
    Guid EmployeeId,
    string? Salutation,
    string FirstName,
    string? MiddleName,
    string LastName,
    string Relationship,
    Gender Gender,
    DateOnly? DateOfBirth,
    BloodGroup BloodGroup,
    string? Nationality,
    string? Occupation,
    bool IsNominee,
    bool IsDependent,
    decimal? NomineePercentage,
    DateTime CreatedDate,
    DateTime? ModifiedDate);
