using HRMS.Domain.Common;
using HRMS.Domain.Enums;

namespace HRMS.Domain.Entities.Separation;

public enum SeparationExitExecutionStatus { NotStarted, InProgress, Completed, Failed, CorrectionRequired }

public enum SeparationExitExecutionEventType { ExitReadinessEvaluated, ExitExecutionStarted, EmploymentExitExecuted, EmployeeInactivated, TenantAccessDeprovisioned, SessionsRevoked, RoleAssignmentsRevoked, ManagerResponsibilitiesReconciled, ExitExecutionFailed, ExitExecutionRetried, SeparationClosed }

public sealed class SeparationExitExecution : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid EmployeeSeparationId { get; set; }
    public Guid EmployeeId { get; set; }
    public SeparationExitExecutionStatus Status { get; set; } = SeparationExitExecutionStatus.NotStarted;
    public DateOnly? FinalLastWorkingDate { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public Guid? StartedByUserId { get; set; }
    public DateTime? EmploymentExecutedAtUtc { get; set; }
    public DateTime? AccessDeprovisionedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? FailedAtUtc { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }
    public string? IdempotencyKey { get; set; }
    public string? SnapshotJson { get; set; }
    public int ConcurrencyVersion { get; set; } = 1;
    public Tenant? Tenant { get; set; }
    public EmployeeSeparation? EmployeeSeparation { get; set; }
    public ICollection<SeparationExitExecutionEvent> Events { get; set; } = new List<SeparationExitExecutionEvent>();
}

public sealed class SeparationExitExecutionEvent : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid SeparationExitExecutionId { get; set; }
    public SeparationExitExecutionEventType EventType { get; set; }
    public Guid? ActorUserId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public string? Step { get; set; }
    public string? Reason { get; set; }
    public string? MetadataJson { get; set; }
    public Tenant? Tenant { get; set; }
    public SeparationExitExecution? Execution { get; set; }
}
