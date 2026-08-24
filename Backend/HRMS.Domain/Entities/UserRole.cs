using HRMS.Domain.Common;

namespace HRMS.Domain.Entities;

/// <summary>
/// Assigns a <see cref="Role"/> to a <see cref="User"/> within a tenant.
/// Composite primary key (UserId, RoleId). TenantId always mirrors the owning user's tenant and
/// participates in tenant isolation.
/// </summary>
public class UserRole : ITenantEntity
{
    public Guid UserId { get; set; }

    public int RoleId { get; set; }

    public Guid TenantId { get; set; }

    // Navigation
    public User? User { get; set; }
    public Role? Role { get; set; }
}
