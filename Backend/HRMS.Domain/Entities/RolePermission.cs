namespace HRMS.Domain.Entities;

/// <summary>
/// Grants a <see cref="Permission"/> to a <see cref="Role"/>. Shared reference data (not
/// tenant-scoped). Composite primary key (RoleId, PermissionId).
/// </summary>
public class RolePermission
{
    public int RoleId { get; set; }

    public int PermissionId { get; set; }

    // Navigation
    public Role? Role { get; set; }
    public Permission? Permission { get; set; }
}
