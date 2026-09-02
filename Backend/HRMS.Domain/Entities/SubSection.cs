using HRMS.Domain.Common;

namespace HRMS.Domain.Entities;

/// <summary>
/// A sub-section within a <see cref="Section"/>. May optionally belong to a section.
/// </summary>
public class SubSection : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    /// <summary>Short identifier, e.g. "SS-01". Unique within the tenant.</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    public Guid? SectionId { get; set; }

    // Navigation
    public Tenant? Tenant { get; set; }
    public Section? Section { get; set; }
}
