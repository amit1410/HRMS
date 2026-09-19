using HRMS.Domain.Common;
using HRMS.Domain.Enums;

namespace HRMS.Domain.Entities;

public sealed class EmployeeSalaryAssignment : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid SalaryStructureId { get; set; }
    public Guid SalaryStructureVersionId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public decimal? AnnualCtc { get; set; }
    public decimal? MonthlyCtc { get; set; }
    public string CurrencyCode { get; set; } = "INR";
    public SalaryPayFrequency PayFrequency { get; set; } = SalaryPayFrequency.Monthly;
    public EmployeeSalaryAssignmentStatus Status { get; set; } = EmployeeSalaryAssignmentStatus.Active;
    public SalaryChangeReason ChangeReason { get; set; } = SalaryChangeReason.Other;
    public string? Remarks { get; set; }
    public int ConcurrencyVersion { get; set; } = 1;
    public Tenant? Tenant { get; set; }
    public Employee? Employee { get; set; }
    public SalaryStructure? SalaryStructure { get; set; }
    public SalaryStructureVersion? SalaryStructureVersion { get; set; }
    public ICollection<EmployeeSalaryComponent> Components { get; set; } = new List<EmployeeSalaryComponent>();
    public ICollection<EmployeeSalaryAssignmentHistory> History { get; set; } = new List<EmployeeSalaryAssignmentHistory>();
}

public sealed class EmployeeSalaryComponent : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid EmployeeSalaryAssignmentId { get; set; }
    public Guid SalaryStructureComponentId { get; set; }
    public Guid SalaryComponentId { get; set; }
    public decimal? OverrideValue { get; set; }
    public decimal? OverridePercentage { get; set; }
    public string? OverrideFormula { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Remarks { get; set; }
    public Tenant? Tenant { get; set; }
    public EmployeeSalaryAssignment? Assignment { get; set; }
    public SalaryStructureComponent? SalaryStructureComponent { get; set; }
    public SalaryComponent? SalaryComponent { get; set; }
}

public sealed class EmployeeSalaryAssignmentHistory : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid EmployeeSalaryAssignmentId { get; set; }
    public Guid? ActorUserId { get; set; }
    public EmployeeSalaryAssignmentChangeType ChangeType { get; set; }
    public DateTime ChangedAtUtc { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid SalaryStructureId { get; set; }
    public Guid SalaryStructureVersionId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public decimal? AnnualCtc { get; set; }
    public decimal? MonthlyCtc { get; set; }
    public string CurrencyCode { get; set; } = "INR";
    public SalaryPayFrequency PayFrequency { get; set; }
    public EmployeeSalaryAssignmentStatus Status { get; set; }
    public SalaryChangeReason ChangeReason { get; set; }
    public string? Remarks { get; set; }
    public string ComponentsJson { get; set; } = "[]";
    public Tenant? Tenant { get; set; }
    public EmployeeSalaryAssignment? Assignment { get; set; }
    public User? ActorUser { get; set; }
}
