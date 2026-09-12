using HRMS.Domain.Common;
using HRMS.Domain.Enums;

namespace HRMS.Domain.Entities;

public sealed class AttendanceRegularizationRequest : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public DateOnly BusinessDate { get; set; }
    public AttendanceRegularizationType RequestType { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime? ProposedInAtUtc { get; set; }
    public DateTime? ProposedOutAtUtc { get; set; }
    public AttendanceRequestStatus Status { get; set; } = AttendanceRequestStatus.Pending;
    public int ConcurrencyVersion { get; set; } = 1;
    public Guid SubmittedByUserId { get; set; }
    public DateTime SubmittedAtUtc { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public string? ReviewerComments { get; set; }
    public ICollection<AttendanceRegularizationEvent> Events { get; set; } = new List<AttendanceRegularizationEvent>();
}

public sealed class AttendanceRegularizationEvent : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid AttendanceRegularizationRequestId { get; set; }
    public AttendanceRequestEventType EventType { get; set; }
    public Guid ActorUserId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public string? Comments { get; set; }
    public AttendanceRegularizationRequest? Request { get; set; }
}

public sealed class AttendanceAdjustment : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public DateOnly BusinessDate { get; set; }
    public Guid AttendanceRegularizationRequestId { get; set; }
    public DateTime? EffectiveInAtUtc { get; set; }
    public DateTime? EffectiveOutAtUtc { get; set; }
    public Guid ApprovedByUserId { get; set; }
    public DateTime ApprovedAtUtc { get; set; }
}

public sealed class AttendanceOnDutyRequest : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? Purpose { get; set; }
    public string? Location { get; set; }
    public AttendanceRequestStatus Status { get; set; } = AttendanceRequestStatus.Pending;
    public int ConcurrencyVersion { get; set; } = 1;
    public Guid SubmittedByUserId { get; set; }
    public DateTime SubmittedAtUtc { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public string? ReviewerComments { get; set; }
    public ICollection<AttendanceOnDutyEvent> Events { get; set; } = new List<AttendanceOnDutyEvent>();
}

public sealed class AttendanceOnDutyEvent : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid AttendanceOnDutyRequestId { get; set; }
    public AttendanceRequestEventType EventType { get; set; }
    public Guid ActorUserId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public string? Comments { get; set; }
    public AttendanceOnDutyRequest? Request { get; set; }
}
