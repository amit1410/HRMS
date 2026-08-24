using HRMS.Domain.Common;

namespace HRMS.Domain.Entities;

/// <summary>
/// An organizational unit within a tenant (Engineering, Finance, …). Code and name are unique per tenant,
/// not globally — two organizations may each have a "HR" department.
/// </summary>
public class Department : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    /// <summary>Short human-assigned identifier, e.g. "ENG". Unique within the tenant.</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>
    /// Whether the department may be assigned to employees. Retiring a department is a state change rather
    /// than a delete, so the employees who belonged to it keep an intact history.
    /// </summary>
    public bool IsActive { get; set; } = true;

    // Navigation
    public Tenant? Tenant { get; set; }
    public ICollection<Employee> Employees { get; set; } = new List<Employee>();
}
