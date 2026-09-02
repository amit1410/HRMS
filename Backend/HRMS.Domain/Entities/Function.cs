using HRMS.Domain.Common;

namespace HRMS.Domain.Entities;

/// <summary>
/// A business function within a tenant (e.g. Engineering, Finance, Operations).
/// Independent master — flat, not parented.
/// </summary>
public class Function : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    /// <summary>Short identifier, e.g. "FN-ENG". Unique within the tenant.</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    // Navigation
    public Tenant? Tenant { get; set; }
}
