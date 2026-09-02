using HRMS.Domain.Common;

namespace HRMS.Domain.Entities;

/// <summary>
/// A sub-department within a <see cref="Department"/>.
/// </summary>
public class SubDepartment : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    /// <summary>Short identifier, e.g. "SUB-FE". Unique within the tenant.</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    public Guid DepartmentId { get; set; }

    // Navigation
    public Tenant? Tenant { get; set; }
    public Department? Department { get; set; }
}
