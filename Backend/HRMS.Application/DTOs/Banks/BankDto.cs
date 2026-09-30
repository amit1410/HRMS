using HRMS.Domain.Enums;

namespace HRMS.Application.DTOs.Banks;

/// <summary>
/// A bank as returned to clients. <see cref="EmployeeAccountCount"/> is included because it is what tells a
/// caller whether the bank can be deleted, saving a second round trip to find out.
/// </summary>
public record BankDto(
    Guid Id,
    string Code,
    string Name,
    string? ShortName,
    string? IfscPrefix,
    BankType? BankType,
    string? Country,
    DateOnly? EffectiveFrom,
    string? Remarks,
    bool IsActive,
    int EmployeeAccountCount,
    DateTime CreatedDate,
    DateTime? ModifiedDate);
