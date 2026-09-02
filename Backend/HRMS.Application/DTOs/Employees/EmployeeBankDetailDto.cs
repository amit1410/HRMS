using HRMS.Domain.Enums;

namespace HRMS.Application.DTOs.Employees;

/// <summary>
/// Masked bank-detail read model. <c>IsActive</c> identifies the current record; false records are
/// historical and remain visible so bank-account history is not lost.
/// </summary>
public record EmployeeBankDetailDto(
    Guid Id,
    Guid EmployeeId,
    Guid BankId,
    string BankName,
    string AccountHolderName,
    string MaskedAccountNumber,
    AccountType AccountType,
    AccountPurpose AccountPurpose,
    BankAccountStatus Status,
    string? MaskedIfscCode,
    string? BranchName,
    DateOnly? EffectiveFrom,
    bool IsActive,
    bool HasDocumentOfProof,
    DateTime CreatedDate,
    DateTime? ModifiedDate);
