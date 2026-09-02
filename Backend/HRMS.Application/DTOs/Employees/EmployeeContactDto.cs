namespace HRMS.Application.DTOs.Employees;

public record EmployeeContactDto(
    Guid Id,
    Guid EmployeeId,
    string? OfficialEmail,
    string? PersonalEmail,
    string? AlternateEmail,
    string? OfficialPhone,
    string? PersonalPhone,
    string? EmergencyNumber,
    bool SameAsCurrentAddress,
    DateTime CreatedDate,
    DateTime? ModifiedDate);
