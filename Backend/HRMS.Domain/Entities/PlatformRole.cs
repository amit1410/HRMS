namespace HRMS.Domain.Entities;

public sealed class PlatformRole
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public ICollection<PlatformUserRole> UserRoles { get; set; } = new List<PlatformUserRole>();
    public ICollection<PlatformRolePermission> RolePermissions { get; set; } = new List<PlatformRolePermission>();
}
