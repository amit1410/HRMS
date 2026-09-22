using HRMS.Application.Common;
using HRMS.Domain.Enums;

namespace HRMS.Application.DTOs.Payroll;

public sealed class VariablePayPlanRequest
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public VariablePayPlanType PlanType { get; set; }
    public string CurrencyCode { get; set; } = "INR";
    public bool IsActive { get; set; } = true;
}

public sealed class VariablePayPlanVersionRequest
{
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public VariablePayPlanVersionStatus Status { get; set; } = VariablePayPlanVersionStatus.Published;
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
}

public sealed class VariablePayAwardRequest
{
    public Guid PlanVersionId { get; set; }
    public DateOnly AwardPeriodFrom { get; set; }
    public DateOnly AwardPeriodTo { get; set; }
    public DateOnly EligibilityDate { get; set; }
    public DateOnly? PayoutDate { get; set; }
    public decimal? PerformanceMultiplier { get; set; }
    public decimal? ManualAmount { get; set; }
    public string? Reason { get; set; }
    public string SettlementMethod { get; set; } = "Payroll";
}

public sealed class VariablePayBulkGenerationRequest
{
    public Guid PlanVersionId { get; set; }
    public DateOnly AwardPeriodFrom { get; set; }
    public DateOnly AwardPeriodTo { get; set; }
    public DateOnly EligibilityDate { get; set; }
}

public sealed class VariablePayOverrideRequest { public decimal OverrideAmount { get; set; } public string Reason { get; set; } = string.Empty; }
public sealed class VariablePaySettlementRequest { public VariablePaySettlementType SettlementType { get; set; } public decimal Amount { get; set; } public DateOnly? SettlementDate { get; set; } public Guid? PayrollRunId { get; set; } public Guid? PayrollResultId { get; set; } public Guid? FinalSettlementId { get; set; } public string? Reference { get; set; } }

public sealed class VariablePayAwardQuery : PagedQuery { public Guid? EmployeeId { get; set; } public Guid? PlanId { get; set; } public VariablePayAwardStatus? Status { get; set; } public DateOnly? PeriodFrom { get; set; } public DateOnly? PeriodTo { get; set; } }

public sealed record VariablePayPlanVersionDto(Guid Id, DateOnly EffectiveFrom, DateOnly? EffectiveTo, VariablePayPlanVersionStatus Status, VariablePayCalculationMethod CalculationMethod, VariablePaySalaryBasisType SalaryBasisType, decimal? Percentage, decimal? FixedAmount, decimal? TargetPercentage, decimal? MinimumAmount, decimal? MaximumAmount, VariablePayProrationMethod ProrationMethod, VariablePayPayoutFrequency PayoutFrequency, VariablePayTaxTreatment TaxTreatment, VariablePayFinalSettlementTreatment FinalSettlementTreatment);
public sealed record VariablePayPlanDto(Guid Id, string Code, string Name, VariablePayPlanType PlanType, string CurrencyCode, bool IsActive, IReadOnlyList<VariablePayPlanVersionDto> Versions);
public sealed record VariablePayAwardDto(Guid Id, Guid EmployeeId, Guid PlanId, Guid PlanVersionId, string AwardNumber, DateOnly AwardPeriodFrom, DateOnly AwardPeriodTo, decimal SalaryBasisAmount, decimal? TargetAmount, decimal? PerformanceMultiplier, decimal ProrationFactor, decimal CalculatedAmount, decimal? ApprovedAmount, decimal TaxableAmount, decimal NonTaxableAmount, decimal SettledAmount, decimal OutstandingAmount, string CurrencyCode, DateOnly? PayoutDate, VariablePayAwardStatus Status, string SettlementMethod, string? Reason);
public sealed record VariablePayAwardHistoryDto(Guid Id, VariablePayHistoryEventType EventType, DateTime OccurredAtUtc, VariablePayAwardStatus? PreviousStatus, VariablePayAwardStatus? NewStatus, decimal? OriginalAmount, decimal? NewAmount, string? Reason);
public sealed record VariablePayBulkGenerationResult(int Eligible, int Generated, int Skipped, IReadOnlyList<string> Reasons);
