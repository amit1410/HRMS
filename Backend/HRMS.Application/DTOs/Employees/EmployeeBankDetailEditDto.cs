using HRMS.Domain.Enums;

namespace HRMS.Application.DTOs.Employees;

/// <summary>Full bank values returned only to callers authorized to edit sensitive employee data.</summary>
public record EmployeeBankDetailEditDto(
    Guid Id,
    Guid EmployeeId,
    Guid BankId,
    string BankName,
    string AccountHolderName,
    string AccountNumber,
    AccountType AccountType,
    AccountPurpose AccountPurpose,
    BankAccountStatus Status,
    string? IfscCode,
    string? BranchName,
    DateOnly? EffectiveFrom,
    bool IsActive,
    string? DocumentOfProof,
    DateTime CreatedDate,
    DateTime? ModifiedDate);
