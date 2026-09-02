using HRMS.Domain.Common;
using HRMS.Domain.Enums;

namespace HRMS.Domain.Entities;

/// <summary>
/// A previous employer record for an employee. Multiple previous employers are supported.
/// </summary>
public class EmployeePreviousEmployment : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    public Guid EmployeeId { get; set; }

    public string Company { get; set; } = string.Empty;

    public string? Designation { get; set; }

    public string? Location { get; set; }

    public EmploymentType EmploymentType { get; set; } = EmploymentType.FullTime;

    public DateOnly? TenureFrom { get; set; }

    public DateOnly? TenureTill { get; set; }

    /// <summary>Path or reference to a document of proof (offer letter, relieving letter).</summary>
    public string? DocumentOfProof { get; set; }

    public ICollection<EmployeeDocument> SupportingDocuments { get; set; } = new List<EmployeeDocument>();

    // Navigation
    public Tenant? Tenant { get; set; }
    public Employee? Employee { get; set; }
}
