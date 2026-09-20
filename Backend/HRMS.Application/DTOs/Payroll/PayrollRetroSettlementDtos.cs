using HRMS.Application.Common;
using HRMS.Domain.Enums;

namespace HRMS.Application.DTOs.Payroll;

public sealed class PayrollRetroCaseRequest { public Guid EmployeeId { get; set; } public PayrollRetroTriggerType TriggerType { get; set; } public Guid? TriggerSourceId { get; set; } public DateOnly EffectiveFrom { get; set; } public DateOnly? EffectiveTo { get; set; } public string? Notes { get; set; } }
public sealed record PayrollRetroComponentDto(Guid Id, string Code, string Name, decimal OriginalAmount, decimal CorrectedAmount, decimal DifferenceAmount, bool IsEarning, bool IsDeduction, bool IsStatutory);
public sealed record PayrollRetroResultDto(Guid Id, Guid CaseId, Guid OriginalPayrollRunId, Guid OriginalPayrollResultId, DateOnly PeriodStartDate, DateOnly PeriodEndDate, decimal OriginalGross, decimal CorrectedGross, decimal GrossDifference, decimal OriginalDeductions, decimal CorrectedDeductions, decimal DeductionDifference, decimal OriginalNet, decimal CorrectedNet, decimal NetDifference, string CurrencyCode, IReadOnlyList<PayrollRetroComponentDto> Components);
public sealed record PayrollRetroCaseDto(Guid Id, Guid EmployeeId, PayrollRetroTriggerType TriggerType, DateOnly EffectiveFrom, DateOnly? EffectiveTo, PayrollRetroStatus Status, IReadOnlyList<PayrollRetroResultDto> Results);
public sealed class FinalSettlementRequest { public Guid EmployeeId { get; set; } public DateOnly SeparationDate { get; set; } public DateOnly LastWorkingDate { get; set; } public DateOnly SettlementDate { get; set; } public string CurrencyCode { get; set; } = "INR"; }
public sealed class FinalSettlementLineRequest { public FinalSettlementLineType LineType { get; set; } public string ComponentCode { get; set; } = string.Empty; public string Description { get; set; } = string.Empty; public decimal Amount { get; set; } public bool IsEarning { get; set; } public bool IsDeduction { get; set; } public string SourceType { get; set; } = "Manual"; public Guid? SourceId { get; set; } }
public sealed record FinalSettlementLineDto(Guid Id, FinalSettlementLineType LineType, string ComponentCode, string Description, decimal Amount, bool IsEarning, bool IsDeduction, string SourceType, Guid? SourceId);
public sealed record FinalSettlementDto(Guid Id, Guid EmployeeId, DateOnly SeparationDate, DateOnly LastWorkingDate, DateOnly SettlementDate, FinalSettlementStatus Status, decimal GrossPayable, decimal TotalDeductions, decimal NetSettlement, string CurrencyCode, IReadOnlyList<FinalSettlementLineDto> Lines);
