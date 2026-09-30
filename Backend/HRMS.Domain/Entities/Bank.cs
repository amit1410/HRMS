using HRMS.Domain.Common;
using HRMS.Domain.Enums;

namespace HRMS.Domain.Entities;

/// <summary>
/// A bank that an organization can assign to employee bank account records. Code and name are unique
/// per tenant, not globally — two organizations may each have a "SBI" bank of their own.
/// <para>
/// The Bank master is deliberately richer than a plain code/name lookup: it carries an IFSC prefix, a
/// classification, a country and an effective date so payroll and bank-advice generation can reason about
/// it. Retiring a bank is a state change (<see cref="IsActive"/> = false) rather than a delete, so existing
/// <see cref="EmployeeBankDetail"/> rows keep an intact reference.
/// </para>
/// </summary>
public class Bank : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    /// <summary>Short human-assigned identifier, e.g. "SBI" (BankCode). Unique within the tenant.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Full bank name (BankName).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Legacy free-text notes column retained for backward compatibility with earlier data and the bank
    /// dropdown seed. New records use <see cref="Remarks"/>; the API surface no longer writes here.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>Optional short/display name, e.g. "SBI" for "State Bank of India".</summary>
    public string? ShortName { get; set; }

    /// <summary>Optional IFSC prefix (the leading bank identifier of an IFSC code, e.g. "SBIN").</summary>
    public string? IfscPrefix { get; set; }

    /// <summary>Optional classification of the bank.</summary>
    public BankType? BankType { get; set; }

    /// <summary>Optional country the bank operates in, stored as free text.</summary>
    public string? Country { get; set; }

    /// <summary>Date from which the bank is considered effective for the organization.</summary>
    public DateOnly? EffectiveFrom { get; set; }

    /// <summary>Free-text remarks about the bank.</summary>
    public string? Remarks { get; set; }

    /// <summary>
    /// Whether the bank may be assigned to new employee bank records. Retiring a bank is a state change
    /// rather than a delete, so existing employee bank details keep an intact reference to it.
    /// </summary>
    public bool IsActive { get; set; } = true;

    // Navigation
    public Tenant? Tenant { get; set; }
    public ICollection<EmployeeBankDetail> EmployeeBankDetails { get; set; } = new List<EmployeeBankDetail>();
}
