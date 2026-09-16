using HRMS.Domain.Common;
using HRMS.Domain.Enums;

namespace HRMS.Domain.Entities;

/// <summary>
/// Assigns a <see cref="Role"/> to a <see cref="User"/> within a tenant.
/// The authoritative, tenant-scoped effective-dated role assignment. The legacy table is evolved
/// in place so existing assignments remain intact while each assignment receives an audit trail.
/// </summary>
public class UserRole : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }

    public int RoleId { get; set; }

    public Guid TenantId { get; set; }
    // A directly-created legacy assignment represents an immediately effective assignment.
    // Provisioning and migration code set an explicit effective date; using a wall-clock
    // default here would make assignments unexpectedly future-dated under deterministic clocks.
    public DateOnly EffectiveFrom { get; set; } = DateOnly.MinValue;
    public DateOnly? EffectiveTo { get; set; }
    public RoleAssignmentSource AssignmentSource { get; set; } = RoleAssignmentSource.Manual;
    public Guid? AssignedByUserId { get; set; }
    public string? AssignmentReason { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }

    // Navigation
    public User? User { get; set; }
    public Role? Role { get; set; }
    public ICollection<UserRoleAssignmentScope> Scopes { get; set; } = new List<UserRoleAssignmentScope>();
    public ICollection<UserRoleAssignmentEvent> Events { get; set; } = new List<UserRoleAssignmentEvent>();
}
