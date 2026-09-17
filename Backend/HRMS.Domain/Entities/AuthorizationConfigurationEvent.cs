using HRMS.Domain.Common;
using HRMS.Domain.Enums;

namespace HRMS.Domain.Entities;

public sealed class AuthorizationConfigurationEvent : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public AuthorizationConfigurationEventType EventType { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public Guid? EntityId { get; set; }
    public int? RoleId { get; set; }
    public Guid? UserRoleAssignmentId { get; set; }
    public Guid? UserId { get; set; }
    public int? PermissionId { get; set; }
    public string? PermissionCode { get; set; }
    public RoleScopeType? ScopeDimension { get; set; }
    public Guid? ScopeValueId { get; set; }
    public string? ScopeValueDisplay { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string? Reason { get; set; }
    public Guid ActorUserId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public Guid? CorrelationId { get; set; }
}
