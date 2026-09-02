using HRMS.Domain.Common;

namespace HRMS.Domain.Entities;

/// <summary>
/// A section within a <see cref="SubDepartment"/>. May optionally belong to a sub-department.
/// </summary>
public class Section : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    /// <summary>Short identifier, e.g. "SEC-01". Unique within the tenant.</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    public Guid? SubDepartmentId { get; set; }

    // Navigation
    public Tenant? Tenant { get; set; }
    public SubDepartment? SubDepartment { get; set; }
}
