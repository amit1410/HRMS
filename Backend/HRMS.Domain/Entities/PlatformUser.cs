namespace HRMS.Domain.Entities;

public sealed class PlatformUser
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public int SecurityRevision { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? LastLoginAtUtc { get; set; }

    public ICollection<PlatformUserRole> UserRoles { get; set; } = new List<PlatformUserRole>();
    public ICollection<PlatformRefreshToken> RefreshTokens { get; set; } = new List<PlatformRefreshToken>();
}
