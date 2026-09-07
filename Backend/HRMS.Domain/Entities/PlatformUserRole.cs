namespace HRMS.Domain.Entities;

public sealed class PlatformUserRole
{
    public Guid PlatformUserId { get; set; }
    public int PlatformRoleId { get; set; }
    public PlatformUser? PlatformUser { get; set; }
    public PlatformRole? PlatformRole { get; set; }
}
