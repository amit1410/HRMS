namespace HRMS.Domain.Entities;

public sealed class PlatformRefreshToken
{
    public Guid Id { get; set; }
    public Guid PlatformUserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public Guid? ReplacedByTokenId { get; set; }
    public PlatformUser? PlatformUser { get; set; }
}
