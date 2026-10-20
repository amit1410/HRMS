using HRMS.Domain.Common;
using HRMS.Domain.Enums;

namespace HRMS.Domain.Entities;

public sealed class WelcomeInvitationBatch : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid RequestedByUserId { get; set; }
    public DateTime RequestedAtUtc { get; set; }
    public int TotalRequested { get; set; }
    public int TotalAccepted { get; set; }
    public int TotalRejected { get; set; }
    public WelcomeInvitationBatchStatus Status { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTime? CompletedAtUtc { get; set; }
    public Tenant? Tenant { get; set; }
    public ICollection<WelcomeInvitationAttempt> Attempts { get; set; } = new List<WelcomeInvitationAttempt>();
}

public sealed class WelcomeInvitationAttempt : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid? PortalUserId { get; set; }
    public Guid? BatchId { get; set; }
    public WelcomeInvitationAttemptType AttemptType { get; set; }
    public WelcomeInvitationStatus DeliveryStatus { get; set; }
    public Guid RequestedByUserId { get; set; }
    public DateTime RequestedAtUtc { get; set; }
    public string RecipientEmailSnapshot { get; set; } = string.Empty;
    public string? ErrorCategory { get; set; }
    public string? ErrorSummary { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public int RetryCount { get; set; }
    public int MaxAttempts { get; set; } = 3;
    public DateTime? NextAttemptAtUtc { get; set; }
    public string? LeaseId { get; set; }
    public DateTime? LeaseExpiresAtUtc { get; set; }
    public DateTime? LastAttemptAtUtc { get; set; }
    public DateTime? ProcessingStartedAtUtc { get; set; }
    public DateTime? SentAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public Tenant? Tenant { get; set; }
    public Employee? Employee { get; set; }
    public User? PortalUser { get; set; }
    public WelcomeInvitationBatch? Batch { get; set; }
}
