using HRMS.Domain.Common;
using HRMS.Domain.Enums;

namespace HRMS.Domain.Entities;

public sealed class GratuityPolicy : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public string CurrencyCode { get; set; } = "INR";
    public int ConcurrencyVersion { get; set; } = 1;
    public Tenant? Tenant { get; set; }
    public ICollection<GratuityPolicyVersion> Versions { get; set; } = new List<GratuityPolicyVersion>();
    public ICollection<SeparationBenefitHistory> History { get; set; } = new List<SeparationBenefitHistory>();
}

public sealed class GratuityPolicyVersion : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid GratuityPolicyId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public GratuityPolicyVersionStatus Status { get; set; } = GratuityPolicyVersionStatus.Draft;
    public int MinimumServiceMonths { get; set; }
    public ServiceRoundingMethod ServiceRoundingMethod { get; set; } = ServiceRoundingMethod.CompletedYearsOnly;
    public decimal? ServiceRoundingThresholdMonths { get; set; }
    public GratuityFormulaType FormulaType { get; set; } = GratuityFormulaType.ServiceYearsBased;
    public decimal? NumeratorDays { get; set; }
    public decimal? DenominatorDays { get; set; }
    public GratuityWageBasisType WageBasisType { get; set; } = GratuityWageBasisType.Basic;
    public string? SelectedSalaryComponentIdsJson { get; set; }
    public decimal? FixedAmount { get; set; }
    public decimal? MaximumBenefit { get; set; }
    public decimal? MinimumBenefit { get; set; }
    public MidpointRounding MonetaryRoundingMethod { get; set; } = MidpointRounding.AwayFromZero;
    public int RoundingPrecision { get; set; } = 2;
    public SeparationBenefitTaxTreatment TaxTreatment { get; set; } = SeparationBenefitTaxTreatment.NonTaxable;
    public decimal? TaxablePercentage { get; set; }
    public bool IsResignationEligible { get; set; } = true;
    public bool IsRetirementEligible { get; set; } = true;
    public bool IsTerminationEligible { get; set; } = true;
    public bool IsDeathEligible { get; set; }
    public bool IsDisabilityEligible { get; set; }
    public bool IsRedundancyEligible { get; set; } = true;
    public bool AllowMinimumServiceOverrideForDeath { get; set; }
    public bool AllowMinimumServiceOverrideForDisability { get; set; }
    public bool IncludeNoticePeriodInService { get; set; }
    public bool LeaveEncashmentEnabled { get; set; }
    public string? LeaveTypeIdsJson { get; set; }
    public decimal? MaximumLeaveEncashmentDays { get; set; }
    public decimal? LeaveEncashmentDivisor { get; set; }
    public SeparationBenefitTaxTreatment LeaveEncashmentTaxTreatment { get; set; } = SeparationBenefitTaxTreatment.NonTaxable;
    public NoticeSettlementType NoticeSettlementType { get; set; } = NoticeSettlementType.None;
    public decimal? NoticeDivisor { get; set; }
    public GratuityWageBasisType NoticeWageBasisType { get; set; } = GratuityWageBasisType.Basic;
    public Tenant? Tenant { get; set; }
    public GratuityPolicy? GratuityPolicy { get; set; }
}

public sealed class GratuityCalculation : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid? FinalSettlementId { get; set; }
    public Guid GratuityPolicyId { get; set; }
    public Guid GratuityPolicyVersionId { get; set; }
    public SeparationReason SeparationReason { get; set; }
    public DateOnly ServiceStartDate { get; set; }
    public DateOnly ServiceEndDate { get; set; }
    public int TotalServiceDays { get; set; }
    public decimal TotalServiceMonths { get; set; }
    public decimal EligibleServiceUnits { get; set; }
    public string AppliedRoundingRule { get; set; } = string.Empty;
    public GratuityWageBasisType WageBasisType { get; set; }
    public decimal WageBasisAmount { get; set; }
    public string WageSnapshotJson { get; set; } = "{}";
    public decimal? NumeratorDays { get; set; }
    public decimal? DenominatorDays { get; set; }
    public decimal GrossCalculatedAmount { get; set; }
    public bool CapApplied { get; set; }
    public decimal? CapAmount { get; set; }
    public decimal FinalGratuityAmount { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal NonTaxableAmount { get; set; }
    public string CurrencyCode { get; set; } = "INR";
    public DateTime CalculationDateUtc { get; set; }
    public Guid? CalculatedByUserId { get; set; }
    public SeparationBenefitCalculationStatus Status { get; set; } = SeparationBenefitCalculationStatus.Calculated;
    public int ConcurrencyVersion { get; set; } = 1;
    public Tenant? Tenant { get; set; }
    public Employee? Employee { get; set; }
    public FinalSettlementCase? FinalSettlement { get; set; }
    public GratuityPolicy? GratuityPolicy { get; set; }
    public GratuityPolicyVersion? GratuityPolicyVersion { get; set; }
}

public sealed class GratuityOverride : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid GratuityCalculationId { get; set; }
    public decimal OriginalAmount { get; set; }
    public decimal OverrideAmount { get; set; }
    public string Reason { get; set; } = string.Empty;
    public Guid? RequestedByUserId { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public Tenant? Tenant { get; set; }
    public GratuityCalculation? GratuityCalculation { get; set; }
}

public sealed class LeaveEncashmentCalculation : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid FinalSettlementId { get; set; }
    public Guid LeaveTypeId { get; set; }
    public decimal EligibleDays { get; set; }
    public decimal EncashableDays { get; set; }
    public decimal WageBasisAmount { get; set; }
    public decimal Divisor { get; set; }
    public decimal GrossAmount { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal NonTaxableAmount { get; set; }
    public string SourceBalanceReference { get; set; } = string.Empty;
    public DateTime CalculationDateUtc { get; set; }
    public Tenant? Tenant { get; set; }
    public Employee? Employee { get; set; }
    public FinalSettlementCase? FinalSettlement { get; set; }
    public LeaveType? LeaveType { get; set; }
}

public sealed class NoticeSettlementCalculation : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid FinalSettlementId { get; set; }
    public NoticeSettlementType Type { get; set; }
    public decimal RequiredDays { get; set; }
    public decimal ServedDays { get; set; }
    public decimal DifferenceDays { get; set; }
    public decimal WageBasisAmount { get; set; }
    public decimal Divisor { get; set; }
    public decimal Amount { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal NonTaxableAmount { get; set; }
    public DateTime CalculationDateUtc { get; set; }
    public Tenant? Tenant { get; set; }
    public Employee? Employee { get; set; }
    public FinalSettlementCase? FinalSettlement { get; set; }
}

public sealed class SeparationBenefitHistory : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid? GratuityPolicyId { get; set; }
    public Guid? GratuityPolicyVersionId { get; set; }
    public Guid? EmployeeId { get; set; }
    public Guid? FinalSettlementId { get; set; }
    public SeparationBenefitHistoryEventType EventType { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public Guid? ActorUserId { get; set; }
    public string? SourceType { get; set; }
    public Guid? SourceId { get; set; }
    public decimal? OriginalAmount { get; set; }
    public decimal? FinalAmount { get; set; }
    public string? Reason { get; set; }
    public string SnapshotJson { get; set; } = "{}";
    public Tenant? Tenant { get; set; }
    public GratuityPolicy? GratuityPolicy { get; set; }
    public GratuityPolicyVersion? GratuityPolicyVersion { get; set; }
    public Employee? Employee { get; set; }
    public FinalSettlementCase? FinalSettlement { get; set; }
}
