using HRMS.Application.Common;
using HRMS.Domain.Enums;

namespace HRMS.Application.DTOs.Payroll;

public sealed class GratuityPolicyRequest
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public string CurrencyCode { get; set; } = "INR";
}

public sealed class GratuityPolicyVersionRequest
{
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public GratuityPolicyVersionStatus Status { get; set; } = GratuityPolicyVersionStatus.Published;
    public int MinimumServiceMonths { get; set; }
    public ServiceRoundingMethod ServiceRoundingMethod { get; set; }
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
}

public sealed class SeparationBenefitCalculationRequest
{
    public Guid? FinalSettlementId { get; set; }
    public DateOnly SeparationDate { get; set; }
    public DateOnly LastWorkingDate { get; set; }
    public SeparationReason SeparationReason { get; set; } = SeparationReason.Other;
    public DateOnly? SettlementDate { get; set; }
    public string CurrencyCode { get; set; } = "INR";
}

public sealed class GratuityOverrideRequest { public decimal OverrideAmount { get; set; } public string Reason { get; set; } = string.Empty; }
public sealed record GratuityPolicyVersionDto(Guid Id, DateOnly EffectiveFrom, DateOnly? EffectiveTo, GratuityPolicyVersionStatus Status, int MinimumServiceMonths, ServiceRoundingMethod ServiceRoundingMethod, GratuityFormulaType FormulaType, decimal? NumeratorDays, decimal? DenominatorDays, GratuityWageBasisType WageBasisType, decimal? MaximumBenefit, decimal? MinimumBenefit, SeparationBenefitTaxTreatment TaxTreatment, bool LeaveEncashmentEnabled, NoticeSettlementType NoticeSettlementType);
public sealed record GratuityPolicyDto(Guid Id, string Code, string Name, bool IsActive, string CurrencyCode, IReadOnlyList<GratuityPolicyVersionDto> Versions);
public sealed record ServiceLengthDto(DateOnly StartDate, DateOnly EndDate, int TotalServiceDays, decimal TotalServiceMonths, decimal CompletedYears, decimal EligibleServiceUnits, string AppliedRoundingRule);
public sealed record GratuityCalculationDto(Guid Id, Guid EmployeeId, Guid? FinalSettlementId, Guid PolicyId, Guid PolicyVersionId, SeparationReason SeparationReason, ServiceLengthDto ServiceLength, GratuityWageBasisType WageBasisType, decimal WageBasisAmount, decimal GrossCalculatedAmount, bool CapApplied, decimal? CapAmount, decimal FinalGratuityAmount, decimal TaxableAmount, decimal NonTaxableAmount, string CurrencyCode, SeparationBenefitCalculationStatus Status);
public sealed record LeaveEncashmentCalculationDto(Guid Id, Guid LeaveTypeId, decimal EligibleDays, decimal EncashableDays, decimal WageBasisAmount, decimal Divisor, decimal GrossAmount, decimal TaxableAmount, decimal NonTaxableAmount);
public sealed record NoticeSettlementCalculationDto(Guid Id, NoticeSettlementType Type, decimal RequiredDays, decimal ServedDays, decimal DifferenceDays, decimal WageBasisAmount, decimal Divisor, decimal Amount, decimal TaxableAmount, decimal NonTaxableAmount);
public sealed record SeparationBenefitDto(Guid EmployeeId, Guid? FinalSettlementId, GratuityCalculationDto? Gratuity, IReadOnlyList<LeaveEncashmentCalculationDto> LeaveEncashment, NoticeSettlementCalculationDto? Notice, FinalSettlementStatus? FinalSettlementStatus);
public sealed record SeparationBenefitHistoryDto(Guid Id, SeparationBenefitHistoryEventType EventType, DateTime OccurredAtUtc, Guid? ActorUserId, string? SourceType, Guid? SourceId, decimal? OriginalAmount, decimal? FinalAmount, string? Reason);
public sealed class SeparationBenefitQuery : PagedQuery { public Guid? EmployeeId { get; set; } public SeparationReason? SeparationReason { get; set; } public DateOnly? From { get; set; } public DateOnly? To { get; set; } }
public sealed record SeparationBenefitRegisterRowDto(Guid EmployeeId, Guid? FinalSettlementId, string EmployeeCode, string EmployeeName, DateOnly? DateOfJoining, DateOnly ServiceEndDate, SeparationReason SeparationReason, decimal TotalServiceMonths, decimal GratuityAmount, decimal LeaveEncashmentAmount, decimal NoticePayAmount, decimal NoticeRecoveryAmount, decimal TaxableAmount, decimal NonTaxableAmount, decimal NetBenefit, FinalSettlementStatus? FinalSettlementStatus);
