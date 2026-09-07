namespace HRMS.Domain.Entities;

public sealed class PlatformPermission
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public ICollection<PlatformRolePermission> RolePermissions { get; set; } = new List<PlatformRolePermission>();
}
