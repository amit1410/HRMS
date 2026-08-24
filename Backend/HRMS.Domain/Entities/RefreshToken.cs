using HRMS.Domain.Common;

namespace HRMS.Domain.Entities;

/// <summary>
/// A persisted refresh token that lets a client obtain a new short-lived access token without
/// re-entering credentials. Only a SHA-256 hash of the token is stored, so a database leak does not
/// hand out usable tokens. Tokens are single-use: refreshing revokes the presented token and issues a
/// replacement (rotation), which makes token theft detectable.
/// </summary>
public class RefreshToken : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    public Guid UserId { get; set; }

    /// <summary>Uppercase hex SHA-256 of the raw token value. The raw value is never persisted or logged.</summary>
    public string TokenHash { get; set; } = string.Empty;

    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>Set when the token is consumed by a refresh or explicitly revoked by a logout.</summary>
    public DateTime? RevokedAtUtc { get; set; }

    /// <summary>Hash of the token issued in this one's place, so a rotation chain can be traced.</summary>
    public string? ReplacedByTokenHash { get; set; }

    // Navigation
    public User? User { get; set; }

    /// <summary>
    /// True when the token has neither been revoked nor expired. Declared as a method (not a property)
    /// so EF Core never attempts to map it to a column.
    /// </summary>
    public bool IsUsableAt(DateTime utcNow) => RevokedAtUtc is null && ExpiresAtUtc > utcNow;
}
