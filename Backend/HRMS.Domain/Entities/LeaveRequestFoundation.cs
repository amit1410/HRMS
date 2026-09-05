using HRMS.Domain.Common;
using HRMS.Domain.Enums;

namespace HRMS.Domain.Entities;

/// <summary>Proposed runtime Leave request aggregate root. State transitions are implemented later.</summary>
public sealed class LeaveRequest : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid LeaveTypeId { get; set; }
    public Guid LeavePeriodId { get; set; }
    public Guid LeavePolicyVersionId { get; set; }
    public Guid LeavePolicyRuleId { get; set; }
    public Guid EmployeeEmploymentHistoryId { get; set; }
    public Gender PolicyGenderSnapshot { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public decimal RequestedQuantity { get; set; }
    public decimal ChargeableQuantity { get; set; }
    public LeaveRequestStatus Status { get; set; } = LeaveRequestStatus.PendingApproval;
    public DateTime? SubmittedAtUtc { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string PayloadFingerprint { get; set; } = string.Empty;
    public byte[] RowVersion { get; set; } = [];

    public Tenant? Tenant { get; set; }
    public Employee? Employee { get; set; }
    public LeaveType? LeaveType { get; set; }
    public LeavePeriod? LeavePeriod { get; set; }
    public LeavePolicyVersion? LeavePolicyVersion { get; set; }
    public LeavePolicyRule? LeavePolicyRule { get; set; }
    public EmployeeEmploymentHistory? EmployeeEmploymentHistory { get; set; }
    public ICollection<LeaveRequestDay> Days { get; set; } = new List<LeaveRequestDay>();
    public ICollection<LeaveRequestEvent> Events { get; set; } = new List<LeaveRequestEvent>();
}
/// <summary>Persisted date-level calculation snapshot owned by a LeaveRequest.</summary>
public sealed class LeaveRequestDay : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid LeaveRequestId { get; set; }
    public DateOnly Date { get; set; }
    public decimal RequestedQuantity { get; set; }
    public decimal ChargeableQuantity { get; set; }
    public string? DayClassification { get; set; }
    public string? CalculationReason { get; set; }
    public bool IsEmployeeRequested { get; set; } = true;

    public Tenant? Tenant { get; set; }
    public LeaveRequest? LeaveRequest { get; set; }
}

/// <summary>Immutable request business history; workflow-specific events are added later.</summary>
public sealed class LeaveRequestEvent : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid LeaveRequestId { get; set; }
    public LeaveRequestEventType EventType { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public LeaveBalanceActorType ActorType { get; set; }
    public Guid? ActorUserId { get; set; }
    public Guid? ActorEmployeeId { get; set; }
    public string? CorrelationId { get; set; }

    public Tenant? Tenant { get; set; }
    public LeaveRequest? LeaveRequest { get; set; }
    public User? ActorUser { get; set; }
    public Employee? ActorEmployee { get; set; }
}
