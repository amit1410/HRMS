namespace HRMS.Domain.Entities;

public sealed class PlatformPasswordResetToken
{
    public Guid Id { get; set; }
    public Guid PlatformUserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? UsedAtUtc { get; set; }
    public PlatformUser? PlatformUser { get; set; }
}
