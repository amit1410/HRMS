using HRMS.Domain.Enums;

namespace HRMS.Application.DTOs.Employees;

public record EmployeeAddressDto(
    Guid Id,
    Guid EmployeeId,
    AddressType AddressType,
    string? Country,
    string? State,
    string? District,
    string? City,
    string? ZipCode,
    string? AddressLine1,
    string? AddressLine2,
    string? HouseNumber,
    DateTime CreatedDate,
    DateTime? ModifiedDate);
