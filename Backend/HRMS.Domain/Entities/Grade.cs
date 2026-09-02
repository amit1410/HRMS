using HRMS.Domain.Common;

namespace HRMS.Domain.Entities;

/// <summary>
/// A pay grade or level within a tenant (e.g. G1, G2, G3). Display order is controlled by <see cref="SortOrder"/>.
/// </summary>
public class Grade : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    /// <summary>Short identifier, e.g. "G1". Unique within the tenant.</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Determines display order (ascending).</summary>
    public int SortOrder { get; set; }

    // Navigation
    public Tenant? Tenant { get; set; }
}
