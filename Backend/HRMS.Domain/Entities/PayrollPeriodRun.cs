using HRMS.Domain.Common;
using HRMS.Domain.Enums;

namespace HRMS.Domain.Entities;

public sealed class PayrollPeriod : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public PayrollPeriodType PeriodType { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public DateOnly PayDate { get; set; }
    public int FiscalYear { get; set; }
    public int PeriodNumber { get; set; }
    public PayrollPeriodStatus Status { get; set; } = PayrollPeriodStatus.Draft;
    public bool IsActive { get; set; } = true;
    public int ConcurrencyVersion { get; set; } = 1;
    public DateTime? LockedAtUtc { get; set; }
    public Guid? LockedByUserId { get; set; }
    public string? LockReason { get; set; }
    public DateTime? UnlockedAtUtc { get; set; }
    public Guid? UnlockedByUserId { get; set; }
    public string? UnlockReason { get; set; }
    public Tenant? Tenant { get; set; }
    public ICollection<PayrollRun> Runs { get; set; } = new List<PayrollRun>();
    public ICollection<PayrollPeriodHistory> History { get; set; } = new List<PayrollPeriodHistory>();
}

public sealed class PayrollPeriodHistory : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid PayrollPeriodId { get; set; }
    public Guid? ActorUserId { get; set; }
    public string ChangeType { get; set; } = string.Empty;
    public DateTime ChangedAtUtc { get; set; }
    public PayrollPeriodStatus Status { get; set; }
    public string SnapshotJson { get; set; } = "{}";
    public PayrollPeriod? PayrollPeriod { get; set; }
    public User? ActorUser { get; set; }
}

public sealed class PayrollRun : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid PayrollPeriodId { get; set; }
    public string RunNumber { get; set; } = string.Empty;
    public PayrollRunType RunType { get; set; } = PayrollRunType.Regular;
    public PayrollRunStatus Status { get; set; } = PayrollRunStatus.Draft;
    public DateTime? StartedAtUtc { get; set; }
    public Guid? StartedByUserId { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public Guid? CompletedByUserId { get; set; }
    public DateTime? LockedAtUtc { get; set; }
    public Guid? LockedByUserId { get; set; }
    public int EmployeeCount { get; set; }
    public string? Notes { get; set; }
    public int ConcurrencyVersion { get; set; } = 1;
    public Tenant? Tenant { get; set; }
    public PayrollPeriod? PayrollPeriod { get; set; }
    public ICollection<PayrollRunEmployee> Employees { get; set; } = new List<PayrollRunEmployee>();
    public ICollection<PayrollRunHistory> History { get; set; } = new List<PayrollRunHistory>();
}

public sealed class PayrollRunEmployee : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid PayrollRunId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid? EmployeeSalaryAssignmentId { get; set; }
    public Guid? SalaryStructureId { get; set; }
    public Guid? SalaryStructureVersionId { get; set; }
    public DateOnly EmploymentSnapshotDate { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid? CostCenterId { get; set; }
    public bool IsEligible { get; set; }
    public string? ExclusionReason { get; set; }
    public PayrollRunEmployeeStatus Status { get; set; }
    public Employee? Employee { get; set; }
    public PayrollRun? PayrollRun { get; set; }
    public EmployeeSalaryAssignment? EmployeeSalaryAssignment { get; set; }
}

public sealed class PayrollRunHistory : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid PayrollRunId { get; set; }
    public Guid? ActorUserId { get; set; }
    public PayrollRunHistoryChangeType ChangeType { get; set; }
    public PayrollRunStatus Status { get; set; }
    public DateTime ChangedAtUtc { get; set; }
    public string? Reason { get; set; }
    public PayrollRun? PayrollRun { get; set; }
    public User? ActorUser { get; set; }
}
