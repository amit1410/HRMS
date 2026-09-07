namespace HRMS.Domain.Entities;

public sealed class PlatformRolePermission
{
    public int PlatformRoleId { get; set; }
    public int PlatformPermissionId { get; set; }
    public PlatformRole? PlatformRole { get; set; }
    public PlatformPermission? PlatformPermission { get; set; }
}
