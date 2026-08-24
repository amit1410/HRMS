using HRMS.Domain.Common;

namespace HRMS.Domain.Entities;

/// <summary>
/// A job title an employee can hold (Software Engineer, HR Manager, …). Independent of
/// <see cref="Department"/>: the same designation may exist in several departments, so the two are
/// assigned separately rather than nested.
/// </summary>
public class Designation : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    /// <summary>Short human-assigned identifier, e.g. "SE2". Unique within the tenant.</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    // Navigation
    public Tenant? Tenant { get; set; }
    public ICollection<Employee> Employees { get; set; } = new List<Employee>();
}
