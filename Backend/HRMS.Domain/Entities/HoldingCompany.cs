using HRMS.Domain.Common;

namespace HRMS.Domain.Entities;

/// <summary>
/// A top-level holding company or parent entity within a tenant's organizational hierarchy.
/// </summary>
public class HoldingCompany : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    /// <summary>Short identifier, e.g. "HC01". Unique within the tenant.</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    // Navigation
    public Tenant? Tenant { get; set; }
}
