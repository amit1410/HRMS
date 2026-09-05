using HRMS.Domain.Enums;

namespace HRMS.Application.DTOs.Leave;

public sealed record LeaveRequestValidationInput(
    Guid LeaveTypeId,
    DateOnly StartDate,
    DateOnly EndDate,
    string IdempotencyKey);

public sealed record LeaveRequestValidationResult(
    Guid EmployeeId,
    Guid LeaveTypeId,
    Guid EmployeeEmploymentHistoryId,
    Guid LeavePeriodId,
    Guid LeavePolicyVersionId,
    Guid LeavePolicyRuleId,
    Gender PolicyGenderSnapshot,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal RequestedQuantity,
    decimal ChargeableQuantity,
    IReadOnlyList<LeaveRequestDayValidationResult> RequestDays,
    EntitlementMode EntitlementMode,
    bool BalanceReservationRequired,
    bool AttachmentRequired,
    string IdempotencyKey,
    string PayloadFingerprint,
    int PolicyPriority,
    int PolicySpecificity);

public sealed record LeaveRequestDayValidationResult(
    DateOnly Date,
    decimal RequestedQuantity,
    decimal ChargeableQuantity,
    string? DayClassification,
    string? CalculationReason,
    bool IsEmployeeRequested);

public sealed record LeaveRequestPreviewRequest(
    Guid LeaveTypeId,
    DateOnly StartDate,
    DateOnly EndDate,
    string IdempotencyKey);

public sealed record LeaveRequestPreviewResponse(
    Guid EmployeeId,
    Guid LeaveTypeId,
    Guid LeavePeriodId,
    Guid LeavePolicyVersionId,
    Guid LeavePolicyRuleId,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal RequestedQuantity,
    decimal ChargeableQuantity,
    IReadOnlyList<LeaveRequestPreviewDay> RequestDays,
    EntitlementMode EntitlementMode,
    bool BalanceReservationRequired,
    bool AttachmentRequired,
    string PayloadFingerprint);

public sealed record LeaveRequestPreviewDay(
    DateOnly Date,
    decimal RequestedQuantity,
    decimal ChargeableQuantity,
    string? DayClassification,
    string? CalculationReason,
    bool IsEmployeeRequested);

public sealed record LeaveRequestSubmissionRequest(
    Guid LeaveTypeId,
    DateOnly StartDate,
    DateOnly EndDate,
    string IdempotencyKey);

public sealed record LeaveRequestSubmissionResponse(
    Guid RequestId,
    LeaveRequestStatus Status,
    Guid EmployeeId,
    Guid LeaveTypeId,
    Guid LeavePeriodId,
    Guid LeavePolicyVersionId,
    Guid LeavePolicyRuleId,
    Guid EmployeeEmploymentHistoryId,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal RequestedQuantity,
    decimal ChargeableQuantity,
    DateTime SubmittedAtUtc,
    IReadOnlyList<LeaveRequestSubmissionDayResponse> RequestDays,
    bool IsReplay);

public sealed record LeaveRequestSubmissionDayResponse(
    DateOnly Date,
    decimal RequestedQuantity,
    decimal ChargeableQuantity,
    string? DayClassification,
    string? CalculationReason,
    bool IsEmployeeRequested);
