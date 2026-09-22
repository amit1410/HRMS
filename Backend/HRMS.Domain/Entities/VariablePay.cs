using HRMS.Domain.Common;
using HRMS.Domain.Enums;

namespace HRMS.Domain.Entities;

public sealed class VariablePayPlan : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public VariablePayPlanType PlanType { get; set; }
    public string CurrencyCode { get; set; } = "INR";
    public bool IsActive { get; set; } = true;
    public int ConcurrencyVersion { get; set; } = 1;
    public Tenant? Tenant { get; set; }
    public ICollection<VariablePayPlanVersion> Versions { get; set; } = new List<VariablePayPlanVersion>();
}

public sealed class VariablePayNumberSequence : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public int Year { get; set; }
    public long NextValue { get; set; } = 1;
    public int ConcurrencyVersion { get; set; } = 1;
    public Tenant? Tenant { get; set; }
}

public sealed class VariablePayPlanVersion : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid VariablePayPlanId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public VariablePayPlanVersionStatus Status { get; set; } = VariablePayPlanVersionStatus.Draft;
    public VariablePayCalculationMethod CalculationMethod { get; set; }
    public VariablePaySalaryBasisType SalaryBasisType { get; set; } = VariablePaySalaryBasisType.FixedConfiguredAmount;
    public decimal? Percentage { get; set; }
    public decimal? FixedAmount { get; set; }
    public decimal? TargetPercentage { get; set; }
    public decimal? MinimumAmount { get; set; }
    public decimal? MaximumAmount { get; set; }
    public decimal? PerformanceMultiplierMinimum { get; set; }
    public decimal? PerformanceMultiplierMaximum { get; set; }
    public VariablePayProrationMethod ProrationMethod { get; set; }
    public VariablePayEligibilityMethod EligibilityMethod { get; set; } = VariablePayEligibilityMethod.ActiveEmployment;
    public int? MinimumServiceMonths { get; set; }
    public VariablePayPayoutFrequency PayoutFrequency { get; set; } = VariablePayPayoutFrequency.Annual;
    public int? PayoutMonth { get; set; }
    public VariablePayTaxTreatment TaxTreatment { get; set; } = VariablePayTaxTreatment.Taxable;
    public decimal? TaxablePercentage { get; set; }
    public VariablePayFinalSettlementTreatment FinalSettlementTreatment { get; set; } = VariablePayFinalSettlementTreatment.IncludeOutstanding;
    public bool RequiresApproval { get; set; } = true;
    public bool AllowManualOverride { get; set; }
    public bool PerformanceRatingRequired { get; set; }
    public string? SelectedSalaryComponentIdsJson { get; set; }
    public string? ApplicabilityJson { get; set; }
    public string? Notes { get; set; }
    public Tenant? Tenant { get; set; }
    public VariablePayPlan? VariablePayPlan { get; set; }
}

public sealed class VariablePayAward : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid VariablePayPlanId { get; set; }
    public Guid VariablePayPlanVersionId { get; set; }
    public string AwardNumber { get; set; } = string.Empty;
    public DateOnly AwardPeriodFrom { get; set; }
    public DateOnly AwardPeriodTo { get; set; }
    public DateOnly EligibilityDate { get; set; }
    public decimal SalaryBasisAmount { get; set; }
    public decimal? TargetAmount { get; set; }
    public decimal? PerformanceMultiplier { get; set; }
    public decimal ProrationFactor { get; set; } = 1m;
    public decimal CalculatedAmount { get; set; }
    public decimal? ApprovedAmount { get; set; }
    public decimal? TaxableAmount { get; set; }
    public decimal? NonTaxableAmount { get; set; }
    public decimal SettledAmount { get; set; }
    public string CurrencyCode { get; set; } = "INR";
    public DateOnly? PayoutDate { get; set; }
    public string SettlementMethod { get; set; } = "Payroll";
    public VariablePayAwardStatus Status { get; set; } = VariablePayAwardStatus.Draft;
    public string? Reason { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }
    public Guid? SubmittedByUserId { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTime? RejectedAtUtc { get; set; }
    public Guid? RejectedByUserId { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public Guid? CancelledByUserId { get; set; }
    public int ConcurrencyVersion { get; set; } = 1;
    public Tenant? Tenant { get; set; }
    public Employee? Employee { get; set; }
    public VariablePayPlan? VariablePayPlan { get; set; }
    public VariablePayPlanVersion? VariablePayPlanVersion { get; set; }
    public ICollection<VariablePaySettlement> Settlements { get; set; } = new List<VariablePaySettlement>();
    public ICollection<VariablePayAwardHistory> History { get; set; } = new List<VariablePayAwardHistory>();
}

public sealed class VariablePaySettlement : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid VariablePayAwardId { get; set; }
    public Guid EmployeeId { get; set; }
    public VariablePaySettlementType SettlementType { get; set; }
    public decimal Amount { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal NonTaxableAmount { get; set; }
    public DateOnly SettlementDate { get; set; }
    public Guid? PayrollRunId { get; set; }
    public Guid? PayrollResultId { get; set; }
    public Guid? FinalSettlementId { get; set; }
    public string? Reference { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public Tenant? Tenant { get; set; }
    public VariablePayAward? VariablePayAward { get; set; }
    public Employee? Employee { get; set; }
    public PayrollRun? PayrollRun { get; set; }
    public PayrollResult? PayrollResult { get; set; }
    public FinalSettlementCase? FinalSettlement { get; set; }
}

public sealed class VariablePayAwardHistory : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid VariablePayAwardId { get; set; }
    public VariablePayHistoryEventType EventType { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public Guid? ActorUserId { get; set; }
    public VariablePayAwardStatus? PreviousStatus { get; set; }
    public VariablePayAwardStatus? NewStatus { get; set; }
    public decimal? OriginalAmount { get; set; }
    public decimal? NewAmount { get; set; }
    public Guid? VariablePayPlanVersionId { get; set; }
    public string? Reason { get; set; }
    public string? SourceType { get; set; }
    public Guid? SourceId { get; set; }
    public Tenant? Tenant { get; set; }
    public VariablePayAward? VariablePayAward { get; set; }
}
