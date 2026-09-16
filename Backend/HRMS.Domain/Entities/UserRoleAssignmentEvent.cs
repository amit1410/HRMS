using HRMS.Domain.Common;
using HRMS.Domain.Enums;

namespace HRMS.Domain.Entities;

public sealed class UserRoleAssignmentEvent : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid AssignmentId { get; set; }
    public Guid UserId { get; set; }
    public int RoleId { get; set; }
    public UserRoleAssignmentEventType EventType { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public RoleAssignmentSource AssignmentSource { get; set; }
    public string? Reason { get; set; }
    public Guid? PerformedByUserId { get; set; }
    public DateTime OccurredAtUtc { get; set; }

    public UserRole? Assignment { get; set; }
}
