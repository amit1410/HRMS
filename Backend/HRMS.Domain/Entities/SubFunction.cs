using HRMS.Domain.Common;

namespace HRMS.Domain.Entities;

/// <summary>
/// A sub-function within a <see cref="Function"/>. May optionally belong to a function.
/// </summary>
public class SubFunction : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    /// <summary>Short identifier, e.g. "SF-FE". Unique within the tenant.</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    public Guid? FunctionId { get; set; }

    // Navigation
    public Tenant? Tenant { get; set; }
    public Function? Function { get; set; }
}
