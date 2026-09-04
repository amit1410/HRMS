using HRMS.Domain.Common;

namespace HRMS.Domain.Entities;

public sealed class AccountEmployeeLinkEvent : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid SubjectUserId { get; set; }
    public Guid ActorUserId { get; set; }
    public long Sequence { get; set; }
    public string Operation { get; set; } = string.Empty;
    public Guid? PreviousEventId { get; set; }
    public Guid? PreviousLinkId { get; set; }
    public Guid? NewLinkId { get; set; }
    public Guid? BeforeEmployeeId { get; set; }
    public Guid? AfterEmployeeId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;

    public Tenant? Tenant { get; set; }
    public User? SubjectUser { get; set; }
    public User? ActorUser { get; set; }
    public Employee? BeforeEmployee { get; set; }
    public Employee? AfterEmployee { get; set; }
    public AccountEmployeeLinkEvent? PreviousEvent { get; set; }
    public AccountEmployeeLinkEvent? PreviousLink { get; set; }
}
