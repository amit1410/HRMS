using HRMS.Application.Common;
using HRMS.Domain.Enums;

namespace HRMS.Application.DTOs.Separation;

public sealed record SettlementBlockerDto(string Code, string Message);
public sealed record SeparationSettlementReadinessDto(
    Guid SeparationId,
    bool IsReady,
    IReadOnlyList<SettlementBlockerDto> Blockers,
    DateOnly? ApprovedLastWorkingDate,
    SeparationClearanceStatus? ClearanceStatus,
    SeparationExitInterviewStatus? ExitInterviewStatus,
    int? NoticeShortfallDays,
    int PendingAssetRecoveryCount,
    Guid? ExistingFinalSettlementId,
    FinalSettlementStatus? SettlementStatus);

public sealed record SeparationSettlementStatusDto(
    Guid SeparationId,
    Guid? OrchestrationId,
    SeparationSettlementOrchestrationStatus OrchestrationStatus,
    Guid? PayrollFinalSettlementId,
    FinalSettlementStatus? PayrollSettlementStatus,
    DateTime? InitiatedAtUtc,
    DateTime? CompletedAtUtc,
    string? FailureCode,
    string? FailureMessage,
    bool ReadyForFinalExitClosure,
    IReadOnlyList<SettlementBlockerDto> Blockers);

public sealed record SeparationSettlementEventDto(
    SeparationSettlementEventType EventType,
    SeparationSettlementOrchestrationStatus? FromStatus,
    SeparationSettlementOrchestrationStatus? ToStatus,
    Guid? ActorUserId,
    DateTime OccurredAtUtc,
    string? Reason);

public sealed class SettlementInitiationRequest
{
    public int? ExpectedConcurrencyVersion { get; set; }
    public string? IdempotencyKey { get; set; }
}

public sealed class SettlementRetryRequest
{
    public string? Reason { get; set; }
}

public sealed class SettlementDashboardQuery : PagedQuery
{
    public string? Status { get; set; }
    public string? EmployeeSearch { get; set; }
    public DateOnly? LwdFrom { get; set; }
    public DateOnly? LwdTo { get; set; }
}

public sealed record SeparationSettlementDashboardItemDto(
    Guid SeparationId,
    Guid EmployeeId,
    string? EmployeeCode,
    string EmployeeName,
    DateOnly? ApprovedLastWorkingDate,
    SeparationSettlementOrchestrationStatus OrchestrationStatus,
    FinalSettlementStatus? PayrollSettlementStatus,
    bool IsReady,
    int BlockerCount,
    DateTime? InitiatedAtUtc,
    DateTime? CompletedAtUtc,
    bool ReadyForFinalExitClosure);
