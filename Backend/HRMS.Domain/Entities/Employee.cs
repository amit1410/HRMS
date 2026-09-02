using HRMS.Domain.Common;
using HRMS.Domain.Enums;

namespace HRMS.Domain.Entities;

/// <summary>
/// A person employed by a tenant. Distinct from <see cref="User"/> on purpose: a user is a login, an
/// employee is an HR record. Most employees never sign in, and a login (an external auditor, a support
/// account) need not correspond to an employee — linking the two is a later concern.
/// <para>
/// Compensation is deliberately absent: it belongs with payroll, where it needs its own permission and a
/// change history. Storing a bare current-salary column here would expose it to everyone holding
/// <c>Employee.View</c> and lose every change ever made to it.
/// </para>
/// </summary>
public class Employee : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    /// <summary>The organization's own identifier for this employee, e.g. "EMP-001". Unique within the tenant.</summary>
    public string? EmployeeCode { get; set; }

    /// <summary>Salutation, e.g. "Mr.", "Mrs.", "Dr.", "Ms.".</summary>
    public string? Salutation { get; set; }

    public string FirstName { get; set; } = string.Empty;

    public string? MiddleName { get; set; }

    public string LastName { get; set; } = string.Empty;

    /// <summary>Work email address. Unique per tenant, and unrelated to any <see cref="User"/> login.</summary>
    public string Email { get; set; } = string.Empty;

    public string? Phone { get; set; }

    public DateOnly? DateOfBirth { get; set; }

    public Gender Gender { get; set; } = Gender.Unspecified;

    public BloodGroup BloodGroup { get; set; } = BloodGroup.Unspecified;

    public MaritalStatus MaritalStatus { get; set; } = MaritalStatus.Unspecified;

    public string? BirthCountry { get; set; }

    public string? BirthState { get; set; }

    public string? BirthCity { get; set; }

    public Guid? BirthCountryId { get; set; }

    public Guid? BirthStateId { get; set; }

    public Guid? BirthCityId { get; set; }

    public string? Religion { get; set; }

    public string? Caste { get; set; }

    public DateOnly DateOfJoining { get; set; }

    /// <summary>Group date of joining — the date the employee joined the parent group/company.</summary>
    public DateOnly? GroupDateOfJoining { get; set; }

    /// <summary>Last working day. Set once the employee is no longer <see cref="EmployeeStatus.Active"/>.</summary>
    public DateOnly? DateOfLeaving { get; set; }

    public EmployeeStatus Status { get; set; } = EmployeeStatus.Active;

    public string? JobStatus { get; set; }

    public string? GroupId { get; set; }

    /// <summary>
    /// Departments and designations belong to the Employment/Position model and are captured in a later
    /// section; they are nullable so an employee can be created from Personal Details alone.
    /// </summary>
    public Guid? DepartmentId { get; set; }

    public Guid? DesignationId { get; set; }

    /// <summary>
    /// The employee this person reports to, within the same tenant. Optional — the top of the reporting
    /// line reports to nobody.
    /// </summary>
    public Guid? ReportingManagerId { get; set; }

    public string? Address { get; set; }

    // --- Sensitive identifiers (masked in API responses) ---

    public string? AadhaarNumber { get; set; }

    public string? PanNumber { get; set; }

    public string? PfNumber { get; set; }

    public string? UanNumber { get; set; }

    public string? EsicNumber { get; set; }

    public string? MediclaimNumber { get; set; }

    public bool Gratuity { get; set; }

    public bool Pension { get; set; }

    public string? CostCenterCode { get; set; }

    /// <summary>FK to master CostCenter. Supersedes <see cref="CostCenterCode"/>.</summary>
    public Guid? CostCenterId { get; set; }

    public string? PayrollLocation { get; set; }

    public bool EsicApplicable { get; set; }

    public string? Citizenship { get; set; }

    public string? LanguageKnown { get; set; }

    public string? ProfilePictureUrl { get; set; }

    /// <summary>Employee type (e.g. "Permanent", "Contract").</summary>
    public string? EmployeeType { get; set; }

    /// <summary>FK to master EmployeeType. Supersedes <see cref="EmployeeType"/>.</summary>
    public Guid? EmployeeTypeId { get; set; }

    /// <summary>
    /// Bitwise flags indicating which supervisor/manager types this employee is eligible for.
    /// Uses <see cref="SupervisorType"/> flags enum (L1, L2, L3, Other, HR, Time).
    /// A value of 0 means the employee is not eligible for any supervisor role.
    /// </summary>
    public SupervisorType ManagerCategories { get; set; } = SupervisorType.None;

    // Navigation
    public Tenant? Tenant { get; set; }
    public Department? Department { get; set; }
    public Designation? Designation { get; set; }
    public Employee? ReportingManager { get; set; }
    public Country? BirthCountryRef { get; set; }
    public State? BirthStateRef { get; set; }
    public City? BirthCityRef { get; set; }
    public EmployeeType? EmployeeTypeRef { get; set; }
    public CostCenter? CostCenterRef { get; set; }
    public ICollection<Employee> DirectReports { get; set; } = new List<Employee>();

    // Child entity collections
    public EmployeeContact? Contact { get; set; }
    public EmployeeSupervisor? Supervisor { get; set; }
    public EmployeeAdditionalInfo? AdditionalInfo { get; set; }
    public ICollection<EmployeeAddress> Addresses { get; set; } = new List<EmployeeAddress>();
    public ICollection<EmployeeFamily> FamilyMembers { get; set; } = new List<EmployeeFamily>();
    public ICollection<EmployeeEducation> EducationRecords { get; set; } = new List<EmployeeEducation>();
    public ICollection<EmployeeEmploymentHistory> EmploymentHistory { get; set; } = new List<EmployeeEmploymentHistory>();
    public EmployeeEmployment? Employment { get; set; }
    public ICollection<EmployeePreviousEmployment> PreviousEmployments { get; set; } = new List<EmployeePreviousEmployment>();
    public ICollection<EmployeeBankDetail> BankDetails { get; set; } = new List<EmployeeBankDetail>();
    public ICollection<EmployeeDocument> Documents { get; set; } = new List<EmployeeDocument>();
    public ICollection<EmployeeAuditLog> AuditLogs { get; set; } = new List<EmployeeAuditLog>();
}
