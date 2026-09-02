using HRMS.Domain.Common;

namespace HRMS.Domain.Entities;

/// <summary>
/// The type of employment contract an employee holds (e.g. Permanent, Contract, Intern).
/// Replaces the free-text <c>EmployeeType</c> string on <see cref="Employee"/>.
/// </summary>
public class EmployeeType : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    /// <summary>Short identifier, e.g. "FT", "CT". Unique within the tenant.</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Determines display order (ascending).</summary>
    public int SortOrder { get; set; }

    // Navigation
    public Tenant? Tenant { get; set; }
}
