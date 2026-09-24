using HRMS.Domain.Common;
using HRMS.Domain.Enums;

namespace HRMS.Domain.Entities.Separation;

public sealed class SeparationSettlementOrchestration : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid EmployeeSeparationId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid? PayrollFinalSettlementId { get; set; }
    public SeparationSettlementOrchestrationStatus Status { get; set; } = SeparationSettlementOrchestrationStatus.NotReady;
    public DateTime? ReadinessCheckedAtUtc { get; set; }
    public DateTime? InitiatedAtUtc { get; set; }
    public Guid? InitiatedByUserId { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? FailedAtUtc { get; set; }
    public string? LastFailureCode { get; set; }
    public string? LastFailureMessage { get; set; }
    public string? IdempotencyKey { get; set; }
    public DateOnly? ApprovedLastWorkingDateSnapshot { get; set; }
    public DateOnly? NoticeStartDateSnapshot { get; set; }
    public int? RequiredNoticeDaysSnapshot { get; set; }
    public int? ServedNoticeDaysSnapshot { get; set; }
    public int WaivedNoticeDaysSnapshot { get; set; }
    public int? NoticeShortfallDaysSnapshot { get; set; }
    public Guid? ClearanceIdSnapshot { get; set; }
    public DateTime? ClearanceCompletedAtSnapshotUtc { get; set; }
    public int PendingAssetRecoveryCountSnapshot { get; set; }
    public Guid? ExitInterviewIdSnapshot { get; set; }
    public string? ExitInterviewDispositionSnapshot { get; set; }
    public int ConcurrencyVersion { get; set; } = 1;
    public Tenant? Tenant { get; set; }
    public EmployeeSeparation? EmployeeSeparation { get; set; }
    public ICollection<SeparationSettlementEvent> Events { get; set; } = new List<SeparationSettlementEvent>();
}

public sealed class SeparationSettlementEvent : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid SeparationSettlementOrchestrationId { get; set; }
    public SeparationSettlementEventType EventType { get; set; }
    public SeparationSettlementOrchestrationStatus? FromStatus { get; set; }
    public SeparationSettlementOrchestrationStatus? ToStatus { get; set; }
    public Guid? ActorUserId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public string? Reason { get; set; }
    public string? MetadataJson { get; set; }
    public Tenant? Tenant { get; set; }
    public SeparationSettlementOrchestration? Orchestration { get; set; }
}
