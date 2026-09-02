using HRMS.Domain.Common;

namespace HRMS.Domain.Entities;

/// <summary>
/// A line of business within a tenant. May optionally belong to a <see cref="HoldingCompany"/>.
/// </summary>
public class Lob : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    /// <summary>Short identifier, e.g. "LOB-IT". Unique within the tenant.</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    public Guid? HoldingCompanyId { get; set; }

    // Navigation
    public Tenant? Tenant { get; set; }
    public HoldingCompany? HoldingCompany { get; set; }
}
