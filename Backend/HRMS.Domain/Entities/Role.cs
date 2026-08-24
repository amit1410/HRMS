namespace HRMS.Domain.Entities;

/// <summary>
/// A role is shared reference data across the whole platform (not tenant-scoped). Which tenant a
/// user holds a role in is captured on <see cref="UserRole"/> via its TenantId.
/// Uses an int key because roles are a small, stable, seeded set.
/// </summary>
public class Role
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    // Navigation
    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}
