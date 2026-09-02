using HRMS.Domain.Common;

namespace HRMS.Domain.Entities;

/// <summary>
/// A bank that an organization can assign to employee bank account records. Code and name are unique
/// per tenant, not globally — two organizations may each have a "SBI" bank of their own.
/// </summary>
public class Bank : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    /// <summary>Short human-assigned identifier, e.g. "SBI". Unique within the tenant.</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>
    /// Whether the bank may be assigned to new employee bank records. Retiring a bank is a state change
    /// rather than a delete, so existing employee bank details keep an intact reference to it.
    /// </summary>
    public bool IsActive { get; set; } = true;

    // Navigation
    public Tenant? Tenant { get; set; }
    public ICollection<EmployeeBankDetail> EmployeeBankDetails { get; set; } = new List<EmployeeBankDetail>();
}
