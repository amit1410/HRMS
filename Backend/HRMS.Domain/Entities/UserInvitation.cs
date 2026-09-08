using HRMS.Domain.Enums;
using HRMS.Domain.Common;

namespace HRMS.Domain.Entities;

/// <summary>Single-use, tenant-bound invitation metadata. Only a hash of the token is stored.</summary>
public sealed class UserInvitation : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public UserInvitationPurpose Purpose { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? UsedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTime? LastSentAtUtc { get; set; }

    public Tenant? Tenant { get; set; }
    public User? User { get; set; }
}
