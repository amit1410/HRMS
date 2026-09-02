using HRMS.Domain.Enums;

namespace HRMS.Application.DTOs.Employees;

public class EmployeeBankDetailRequest
{
    public Guid BankId { get; set; }
    public string AccountHolderName { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public AccountType AccountType { get; set; } = AccountType.Savings;
    public AccountPurpose AccountPurpose { get; set; } = AccountPurpose.Salary;
    public BankAccountStatus Status { get; set; } = BankAccountStatus.Active;
    public string? IfscCode { get; set; }
    public string? BranchName { get; set; }
    public DateOnly? EffectiveFrom { get; set; }
    public string? DocumentOfProof { get; set; }
}
