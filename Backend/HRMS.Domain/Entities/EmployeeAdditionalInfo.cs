using HRMS.Domain.Common;

namespace HRMS.Domain.Entities;

/// <summary>
/// Additional/supplementary information for an employee that does not fit in the main
/// employee record or other sub-entities.
/// </summary>
public class EmployeeAdditionalInfo : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    public Guid EmployeeId { get; set; }

    /// <summary>Division the employee belongs to.</summary>
    public string? Division { get; set; }

    /// <summary>PA/PSA code.</summary>
    public string? PaPsa { get; set; }

    /// <summary>Additional/alternative employee code.</summary>
    public string? AdditionalEmployeeCode { get; set; }

    /// <summary>Contract identifier, relevant for contract employees.</summary>
    public string? ContractId { get; set; }

    // Navigation
    public Tenant? Tenant { get; set; }
    public Employee? Employee { get; set; }
}
