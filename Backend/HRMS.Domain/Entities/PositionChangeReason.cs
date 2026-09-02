using HRMS.Domain.Common;

namespace HRMS.Domain.Entities;

/// <summary>
/// A tenant-scoped reason for a position change, replacing the fixed <see cref="Enums.EmploymentChangeReason"/> enum
/// with a maintainable master list. Codes are seeded with semantic meaning; tenants may add their own.
/// </summary>
public class PositionChangeReason : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    /// <summary>Short identifier, e.g. "PROMO", "TRANSFER". Unique within the tenant.</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Determines display order (ascending).</summary>
    public int SortOrder { get; set; }

    // Navigation
    public Tenant? Tenant { get; set; }
}
