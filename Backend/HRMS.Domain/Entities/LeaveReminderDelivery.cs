using HRMS.Domain.Common;

namespace HRMS.Domain.Entities;

/// <summary>Durable claim and delivery history for one Leave approval reminder occurrence.</summary>
public sealed class LeaveReminderDelivery : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid LeaveRequestId { get; set; }
    public Guid RecipientEmployeeId { get; set; }
    public string OccurrenceKey { get; set; } = string.Empty;
    public string NotificationType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime DueAtUtc { get; set; }
    public DateTime? ClaimedAtUtc { get; set; }
    public DateTime? LeaseExpiresAtUtc { get; set; }
    public DateTime? SentAtUtc { get; set; }
    public DateTime? NextAttemptAtUtc { get; set; }
    public int AttemptCount { get; set; }
    public string? LastError { get; set; }

    public Tenant? Tenant { get; set; }
    public LeaveRequest? LeaveRequest { get; set; }
    public Employee? RecipientEmployee { get; set; }
}
