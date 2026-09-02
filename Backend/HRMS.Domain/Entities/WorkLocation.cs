using HRMS.Domain.Common;

namespace HRMS.Domain.Entities;

/// <summary>
/// A physical work location within a tenant (e.g. office address, remote designation).
/// </summary>
public class WorkLocation : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    /// <summary>Short identifier, e.g. "WL-MUM". Unique within the tenant.</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    // Navigation
    public Tenant? Tenant { get; set; }
}
