using HRMS.Domain.Enums;

namespace HRMS.Application.DTOs.Employees;

/// <summary>
/// Request to create a new position change for an employee. All organizational fields
/// are FK references to master data, not free-text strings.
/// </summary>
public class EmploymentChangeRequest
{
    /// <summary>Manual Employee Code for a draft employee; ignored once a code is assigned.</summary>
    public string? EmployeeCode { get; set; }
    public DateOnly EffectiveFrom { get; set; }

    // Organizational FK references
    public Guid? HoldingCompanyId { get; set; }
    public Guid? LobId { get; set; }
    public Guid? OrganisationId { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid? SubDepartmentId { get; set; }
    public Guid? SectionId { get; set; }
    public Guid? SubSectionId { get; set; }
    public Guid? FunctionId { get; set; }
    public Guid? SubFunctionId { get; set; }

    // Job classification
    public Guid? GradeId { get; set; }
    public Guid? DesignationId { get; set; }
    public Guid? EmployeeTypeId { get; set; }

    // Location
    public Guid? CountryLocationId { get; set; }
    public Guid? WorkLocationId { get; set; }

    // Cost center
    public Guid? CostCenterId { get; set; }

    // Reporting
    public Guid? ManagerId { get; set; }

    // Change metadata
    public Guid? PositionChangeReasonId { get; set; }
    public EmploymentChangeReason ChangeReason { get; set; } = EmploymentChangeReason.NewJoining;
    public string? ChangeReasonDescription { get; set; }

    // Snapshot fields
    public string? BusinessRole { get; set; }
    public string? GradeLevel { get; set; }
    public string? CareerGroup { get; set; }
    public EmploymentType EmploymentType { get; set; } = EmploymentType.FullTime;
    public EmployeeStatus EmploymentStatus { get; set; } = EmployeeStatus.Active;
}
