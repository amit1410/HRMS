using HRMS.Domain.Common;

namespace HRMS.Domain.Entities;

/// <summary>
/// An organizational unit within a tenant (e.g. a legal entity or subsidiary).
/// Independent master — flat, not parented to HoldingCompany or LOB.
/// </summary>
public class Organisation : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    /// <summary>Short identifier, e.g. "ORG01". Unique within the tenant.</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    // Navigation
    public Tenant? Tenant { get; set; }
}
