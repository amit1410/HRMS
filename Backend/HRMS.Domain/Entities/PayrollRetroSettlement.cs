using HRMS.Domain.Common;
using HRMS.Domain.Enums;

namespace HRMS.Domain.Entities;

public sealed class PayrollRetroCase : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public PayrollRetroTriggerType TriggerType { get; set; }
    public Guid? TriggerSourceId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public DateTime DetectedAtUtc { get; set; }
    public Guid? DetectedByUserId { get; set; }
    public PayrollRetroStatus Status { get; set; } = PayrollRetroStatus.Detected;
    public string? Notes { get; set; }
    public Tenant? Tenant { get; set; }
    public Employee? Employee { get; set; }
    public ICollection<PayrollRetroResult> Results { get; set; } = new List<PayrollRetroResult>();
    public ICollection<PayrollRetroHistory> History { get; set; } = new List<PayrollRetroHistory>();
}

public sealed class PayrollRetroResult : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid PayrollRetroCaseId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid OriginalPayrollRunId { get; set; }
    public Guid OriginalPayrollResultId { get; set; }
    public DateOnly PeriodStartDate { get; set; }
    public DateOnly PeriodEndDate { get; set; }
    public decimal OriginalGross { get; set; }
    public decimal CorrectedGross { get; set; }
    public decimal GrossDifference { get; set; }
    public decimal OriginalDeductions { get; set; }
    public decimal CorrectedDeductions { get; set; }
    public decimal DeductionDifference { get; set; }
    public decimal OriginalNet { get; set; }
    public decimal CorrectedNet { get; set; }
    public decimal NetDifference { get; set; }
    public string CurrencyCode { get; set; } = "INR";
    public DateTime CalculatedAtUtc { get; set; }
    public int CalculationVersion { get; set; } = 1;
    public Tenant? Tenant { get; set; }
    public PayrollRetroCase? RetroCase { get; set; }
    public Employee? Employee { get; set; }
    public PayrollRun? OriginalPayrollRun { get; set; }
    public PayrollResult? OriginalPayrollResult { get; set; }
    public ICollection<PayrollRetroComponent> Components { get; set; } = new List<PayrollRetroComponent>();
    public ICollection<PayrollAdjustment> Adjustments { get; set; } = new List<PayrollAdjustment>();
}

public sealed class PayrollRetroComponent : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid PayrollRetroResultId { get; set; }
    public Guid? SalaryComponentId { get; set; }
    public StatutoryType? StatutoryType { get; set; }
    public string ComponentCode { get; set; } = string.Empty;
    public string ComponentName { get; set; } = string.Empty;
    public decimal OriginalAmount { get; set; }
    public decimal CorrectedAmount { get; set; }
    public decimal DifferenceAmount { get; set; }
    public bool IsEarning { get; set; }
    public bool IsDeduction { get; set; }
    public bool IsStatutory { get; set; }
    public int Sequence { get; set; }
    public Tenant? Tenant { get; set; }
    public PayrollRetroResult? RetroResult { get; set; }
    public SalaryComponent? SalaryComponent { get; set; }
}

public sealed class PayrollAdjustment : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public Guid SourceId { get; set; }
    public PayrollAdjustmentType AdjustmentType { get; set; }
    public string ComponentCode { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = "INR";
    public Guid? TargetPayrollRunId { get; set; }
    public PayrollAdjustmentStatus Status { get; set; } = PayrollAdjustmentStatus.Unapplied;
    public DateTime? AppliedAtUtc { get; set; }
    public Tenant? Tenant { get; set; }
    public Employee? Employee { get; set; }
    public PayrollRun? TargetPayrollRun { get; set; }
    public PayrollRetroResult? RetroResult { get; set; }
}

public sealed class PayrollRetroHistory : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid PayrollRetroCaseId { get; set; }
    public PayrollRetroHistoryChangeType ChangeType { get; set; }
    public DateTime ChangedAtUtc { get; set; }
    public Guid? ActorUserId { get; set; }
    public string? Message { get; set; }
    public Tenant? Tenant { get; set; }
    public PayrollRetroCase? RetroCase { get; set; }
}

public sealed class FinalSettlementCase : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public DateOnly SeparationDate { get; set; }
    public DateOnly LastWorkingDate { get; set; }
    public DateOnly SettlementDate { get; set; }
    public Guid? PayrollRunId { get; set; }
    public FinalSettlementStatus Status { get; set; } = FinalSettlementStatus.Draft;
    public string CurrencyCode { get; set; } = "INR";
    public decimal GrossPayable { get; set; }
    public decimal TotalDeductions { get; set; }
    public decimal NetSettlement { get; set; }
    public string EmployeeCodeSnapshot { get; set; } = string.Empty;
    public string EmployeeNameSnapshot { get; set; } = string.Empty;
    public string? DepartmentSnapshot { get; set; }
    public string? DesignationSnapshot { get; set; }
    public string? LocationSnapshot { get; set; }
    public DateOnly? DateOfJoiningSnapshot { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTime? FinalizedAtUtc { get; set; }
    public Guid? FinalizedByUserId { get; set; }
    public int ConcurrencyVersion { get; set; } = 1;
    public Tenant? Tenant { get; set; }
    public Employee? Employee { get; set; }
    public PayrollRun? PayrollRun { get; set; }
    public ICollection<FinalSettlementLine> Lines { get; set; } = new List<FinalSettlementLine>();
    public ICollection<FinalSettlementHistory> History { get; set; } = new List<FinalSettlementHistory>();
}

public sealed class FinalSettlementLine : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid FinalSettlementCaseId { get; set; }
    public FinalSettlementLineType LineType { get; set; }
    public string ComponentCode { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public bool IsEarning { get; set; }
    public bool IsDeduction { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public Guid? SourceId { get; set; }
    public int Sequence { get; set; }
    public Tenant? Tenant { get; set; }
    public FinalSettlementCase? Settlement { get; set; }
}

public sealed class FinalSettlementHistory : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid FinalSettlementCaseId { get; set; }
    public FinalSettlementHistoryChangeType ChangeType { get; set; }
    public DateTime ChangedAtUtc { get; set; }
    public Guid? ActorUserId { get; set; }
    public string? Message { get; set; }
    public Tenant? Tenant { get; set; }
    public FinalSettlementCase? Settlement { get; set; }
}
