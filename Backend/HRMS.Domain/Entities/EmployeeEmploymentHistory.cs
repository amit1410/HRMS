using HRMS.Domain.Common;
using HRMS.Domain.Enums;

namespace HRMS.Domain.Entities;

/// <summary>
/// An effective-dated position history record for an employee. Every employment change
/// (department, role, location, grade, manager, etc.) creates a new record rather than
/// overwriting the previous one. The current position is derived from the record where
/// <see cref="EffectiveTo"/> is null.
/// <para>
/// Organizational fields are FK references to tenant-scoped master tables. String columns
/// have been removed; snapshot names (e.g. <see cref="DesignationName"/>) are kept alongside
/// FKs only where historical accuracy requires preserving the value at the time of the record.
/// </para>
/// </summary>
public class EmployeeEmploymentHistory : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    public Guid EmployeeId { get; set; }

    /// <summary>The date from which this position record is effective (inclusive).</summary>
    public DateOnly EffectiveFrom { get; set; }

    /// <summary>
    /// The date until which this position record was effective (inclusive). Null means
    /// this is the current/active record.
    /// </summary>
    public DateOnly? EffectiveTo { get; set; }

    // --- Organizational FK references ---

    public Guid? HoldingCompanyId { get; set; }

    public Guid? LobId { get; set; }

    public Guid? OrganisationId { get; set; }

    public Guid? DepartmentId { get; set; }

    public Guid? SubDepartmentId { get; set; }

    public Guid? SectionId { get; set; }

    public Guid? SubSectionId { get; set; }

    public Guid? FunctionId { get; set; }

    public Guid? SubFunctionId { get; set; }

    // --- Job classification FK references ---

    public Guid? GradeId { get; set; }

    public Guid? DesignationId { get; set; }

    public Guid? EmployeeTypeId { get; set; }

    // --- Location FK references ---

    /// <summary>FK to global Countries table for the country/location at this point in time.</summary>
    public Guid? CountryLocationId { get; set; }

    public Guid? WorkLocationId { get; set; }

    // --- Cost center ---

    public Guid? CostCenterId { get; set; }

    // --- Reporting ---

    public Guid? ManagerId { get; set; }

    // --- Change metadata ---

    public Guid? PositionChangeReasonId { get; set; }

    public EmploymentChangeReason ChangeReason { get; set; } = EmploymentChangeReason.NewJoining;

    public string? ChangeReasonDescription { get; set; }

    /// <summary>User who created this position record (employee code or user email).</summary>
    public string? CreatedBy { get; set; }

    // --- Snapshot fields (kept alongside FK for historical accuracy) ---

    public string? BusinessRole { get; set; }

    public string? GradeLevel { get; set; }

    public string? CareerGroup { get; set; }

    /// <summary>Employment type at the time of this record (e.g. FullTime, Contract).</summary>
    public EmploymentType EmploymentType { get; set; } = EmploymentType.FullTime;

    /// <summary>Employment status at the time of this record.</summary>
    public EmployeeStatus EmploymentStatus { get; set; } = EmployeeStatus.Active;

    /// <summary>Designation name at the time of this record. Snapshot for historical accuracy.</summary>
    public string? DesignationName { get; set; }

    /// <summary>Department name at the time of this record. Snapshot for historical accuracy.</summary>
    public string? DepartmentName { get; set; }

    /// <summary>Manager employee code at the time of this record.</summary>
    public string? ManagerCode { get; set; }

    /// <summary>Manager full name at the time of this record.</summary>
    public string? ManagerName { get; set; }

    // --- Navigation Properties ---

    public Tenant? Tenant { get; set; }
    public Employee? Employee { get; set; }

    // Organizational master navigations
    public HoldingCompany? HoldingCompany { get; set; }
    public Lob? Lob { get; set; }
    public Organisation? Organisation { get; set; }
    public Department? Department { get; set; }
    public SubDepartment? SubDepartment { get; set; }
    public Section? Section { get; set; }
    public SubSection? SubSection { get; set; }
    public Function? Function { get; set; }
    public SubFunction? SubFunction { get; set; }

    // Job classification navigations
    public Grade? Grade { get; set; }
    public Designation? Designation { get; set; }
    public EmployeeType? EmployeeType { get; set; }

    // Location navigations
    public Country? CountryLocation { get; set; }
    public WorkLocation? WorkLocation { get; set; }

    // Cost center navigation
    public CostCenter? CostCenter { get; set; }

    // Reporting navigation
    public Employee? Manager { get; set; }

    // Change reason navigation
    public PositionChangeReason? PositionChangeReason { get; set; }
}
