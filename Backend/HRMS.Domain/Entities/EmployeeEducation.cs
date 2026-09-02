using HRMS.Domain.Common;
using HRMS.Domain.Enums;

namespace HRMS.Domain.Entities;

/// <summary>
/// An education record for an employee. Multiple education records are supported per employee.
/// </summary>
public class EmployeeEducation : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    public Guid EmployeeId { get; set; }

    /// <summary>Education level, e.g. "Bachelor's", "Master's", "PhD".</summary>
    public string EducationLevel { get; set; } = string.Empty;

    /// <summary>Specific qualification or degree, e.g. "B.Tech", "MBA".</summary>
    public string Qualification { get; set; } = string.Empty;

    public string? University { get; set; }

    public string? Institute { get; set; }

    public EducationType EducationType { get; set; } = EducationType.FullTime;

    public string? AreaOfSpecialization { get; set; }

    public int? YearOfPassing { get; set; }

    public string? Score { get; set; }

    /// <summary>Path or reference to a document of proof (certificate, transcript).</summary>
    public string? DocumentOfProof { get; set; }

    // Navigation
    public Tenant? Tenant { get; set; }
    public Employee? Employee { get; set; }
}
