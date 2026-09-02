using HRMS.Domain.Common;
using HRMS.Domain.Enums;

namespace HRMS.Domain.Entities;

/// <summary>
/// A physical address associated with an employee. Each employee can have at most two addresses:
/// one <see cref="AddressType.Current"/> and one <see cref="AddressType.Permanent"/>.
/// </summary>
public class EmployeeAddress : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    public Guid EmployeeId { get; set; }

    public AddressType AddressType { get; set; }

    public string? Country { get; set; }

    public string? State { get; set; }

    public string? District { get; set; }

    public string? City { get; set; }

    public string? ZipCode { get; set; }

    public string? AddressLine1 { get; set; }

    public string? AddressLine2 { get; set; }

    public string? HouseNumber { get; set; }

    // Navigation
    public Tenant? Tenant { get; set; }
    public Employee? Employee { get; set; }
}
