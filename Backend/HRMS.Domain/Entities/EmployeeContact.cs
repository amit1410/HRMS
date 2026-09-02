using HRMS.Domain.Common;

namespace HRMS.Domain.Entities;

/// <summary>
/// Contact information for an employee. A single record per employee holds official and personal
/// contact details, emergency contact, and an indicator for address duplication.
/// </summary>
public class EmployeeContact : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    public Guid EmployeeId { get; set; }

    public string? OfficialEmail { get; set; }

    public string? PersonalEmail { get; set; }

    public string? AlternateEmail { get; set; }

    public string? OfficialPhone { get; set; }

    public string? PersonalPhone { get; set; }

    public string? EmergencyNumber { get; set; }

    /// <summary>When true, the permanent address is identical to the current address.</summary>
    public bool SameAsCurrentAddress { get; set; }

    // Navigation
    public Tenant? Tenant { get; set; }
    public Employee? Employee { get; set; }
}
