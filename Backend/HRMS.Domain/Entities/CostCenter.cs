using HRMS.Domain.Common;

namespace HRMS.Domain.Entities;

/// <summary>
/// A cost center within a tenant for financial allocation and reporting.
/// </summary>
public class CostCenter : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    /// <summary>Short identifier, e.g. "CC-001". Unique within the tenant.</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    // Navigation
    public Tenant? Tenant { get; set; }
}
