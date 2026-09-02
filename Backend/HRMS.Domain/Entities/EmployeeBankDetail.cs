using HRMS.Domain.Common;
using HRMS.Domain.Enums;

namespace HRMS.Domain.Entities;

/// <summary>
/// A bank account record for an employee. Multiple bank accounts are supported one per
/// <see cref="AccountPurpose"/> (salary, gratuity, etc.). The bank itself lives in the tenant-scoped
/// <see cref="Bank"/> master and is referenced by <see cref="BankId"/> — never stored as free text.
/// Sensitive information like full account numbers must be protected.
/// </summary>
public class EmployeeBankDetail : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    public Guid EmployeeId { get; set; }

    /// <summary>Foreign key to the tenant-scoped <see cref="Bank"/> master. Never free text.</summary>
    public Guid BankId { get; set; }

    public string AccountHolderName { get; set; } = string.Empty;

    /// <summary>Account number stored encrypted or masked. Never exposed in full via API.</summary>
    public string AccountNumber { get; set; } = string.Empty;

    public AccountType AccountType { get; set; } = AccountType.Savings;

    public AccountPurpose AccountPurpose { get; set; } = AccountPurpose.Salary;

    public BankAccountStatus Status { get; set; } = BankAccountStatus.Active;

    public string? IfscCode { get; set; }

    public string? BranchName { get; set; }

    /// <summary>Date the account becomes (or became) effective for payroll.</summary>
    public DateOnly? EffectiveFrom { get; set; }

    /// <summary>
    /// Current-record flag. A current record must also have <see cref="BankAccountStatus.Active"/> status;
    /// Frozen/Closed or deactivated records remain in the database as immutable history.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Path or reference to a document of proof (cancelled cheque, passbook).</summary>
    public string? DocumentOfProof { get; set; }

    // Navigation
    public Tenant? Tenant { get; set; }
    public Employee? Employee { get; set; }
    public Bank? Bank { get; set; }
}
