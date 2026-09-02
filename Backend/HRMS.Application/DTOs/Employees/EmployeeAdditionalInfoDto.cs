namespace HRMS.Application.DTOs.Employees;

public record EmployeeAdditionalInfoDto(
    Guid Id,
    Guid EmployeeId,
    string? Division,
    string? PaPsa,
    string? AdditionalEmployeeCode,
    string? ContractId,
    DateTime CreatedDate,
    DateTime? ModifiedDate);
