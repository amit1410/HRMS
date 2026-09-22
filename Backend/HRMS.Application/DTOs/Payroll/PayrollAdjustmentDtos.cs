using HRMS.Application.Common;
using HRMS.Domain.Enums;

namespace HRMS.Application.DTOs.Payroll;

public sealed class PayrollAdjustmentReasonRequest
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public bool RequiresComment { get; set; }
    public string AllowedAdjustmentTypes { get; set; } = "[]";
}

public sealed class PayrollAdjustmentRequest
{
    public Guid EmployeeId { get; set; }
    public PayrollAdjustmentType AdjustmentType { get; set; } = PayrollAdjustmentType.AdditionalEarning;
    public string SourceType { get; set; } = "ManualPayrollCorrection";
    public Guid? SourceReferenceId { get; set; }
    public DateOnly EffectiveDate { get; set; }
    public Guid? PayrollPeriodId { get; set; }
    public Guid? OriginalPayrollRunId { get; set; }
    public Guid? OriginalPayrollResultId { get; set; }
    public Guid? TargetPayrollRunId { get; set; }
    public Guid? SalaryComponentId { get; set; }
    public Guid? ReasonCodeId { get; set; }
    public string ComponentCode { get; set; } = "PAYROLL-ADJUSTMENT";
    public string Description { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = "INR";
    public PayrollAdjustmentDirection Direction { get; set; } = PayrollAdjustmentDirection.Earning;
    public PayrollAdjustmentTaxTreatment TaxTreatment { get; set; } = PayrollAdjustmentTaxTreatment.Taxable;
    public PayrollAdjustmentStatutoryTreatment StatutoryTreatment { get; set; } = PayrollAdjustmentStatutoryTreatment.RecalculateConfiguredStatutory;
    public PayrollAdjustmentSettlementMethod SettlementMethod { get; set; } = PayrollAdjustmentSettlementMethod.Payroll;
}

public sealed class PayrollOffCycleRunRequest
{
    public Guid PayrollPeriodId { get; set; }
    public PayrollRunType RunType { get; set; } = PayrollRunType.OffCycle;
    public DateOnly EffectiveDate { get; set; }
    public DateOnly PaymentDate { get; set; }
    public string? Notes { get; set; }
    public IReadOnlyList<Guid> AdjustmentIds { get; set; } = [];
}

public sealed class PayrollReversalRequest
{
    public Guid OriginalPayrollRunId { get; set; }
    public Guid? OriginalPayrollResultId { get; set; }
    public Guid? ReasonCodeId { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class PayrollAdjustmentQuery : PagedQuery
{
    public Guid? EmployeeId { get; set; }
    public PayrollAdjustmentStatus? Status { get; set; }
    public PayrollAdjustmentType? AdjustmentType { get; set; }
    public PayrollAdjustmentDirection? Direction { get; set; }
}

public sealed record PayrollAdjustmentReasonDto(Guid Id, string Code, string Name, string? Description, bool IsActive, bool RequiresComment, string AllowedAdjustmentTypes);
public sealed record PayrollAdjustmentDto(Guid Id, Guid EmployeeId, string EmployeeCode, string AdjustmentNumber, PayrollAdjustmentType AdjustmentType, string SourceType, Guid? SourceReferenceId, DateOnly EffectiveDate, Guid? OriginalPayrollRunId, Guid? OriginalPayrollResultId, Guid? TargetPayrollRunId, string ComponentCode, string Description, decimal Amount, decimal AppliedAmount, decimal OutstandingAmount, string CurrencyCode, PayrollAdjustmentDirection Direction, PayrollAdjustmentTaxTreatment TaxTreatment, PayrollAdjustmentStatutoryTreatment StatutoryTreatment, PayrollAdjustmentSettlementMethod SettlementMethod, PayrollAdjustmentStatus Status, string? Reason);
public sealed record PayrollAdjustmentHistoryDto(Guid Id, PayrollAdjustmentHistoryEventType EventType, PayrollAdjustmentStatus? PreviousStatus, PayrollAdjustmentStatus? NewStatus, decimal? Amount, string? Reason, DateTime OccurredAtUtc);
public sealed record PayrollReversalDto(Guid Id, Guid OriginalPayrollRunId, Guid? OriginalPayrollResultId, Guid? ReversalRunId, PayrollReversalStatus Status, string Reason, DateTime CreatedDate, DateTime? FinalizedAtUtc);
