namespace HRMS.Domain.Entities;

/// <summary>
/// A fine-grained permission (e.g. "Employee.Create"). Shared reference data, not tenant-scoped.
/// Permissions are granted to roles via <see cref="RolePermission"/> and surfaced on the JWT so
/// the API can authorize by permission rather than hard-coding role checks.
/// </summary>
public class Permission
{
    public int Id { get; set; }

    /// <summary>Canonical permission name in "Resource.Action" form, e.g. "Employee.View".</summary>
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    // Navigation
    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}
