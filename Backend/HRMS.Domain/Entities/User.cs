using HRMS.Domain.Common;

namespace HRMS.Domain.Entities;

/// <summary>
/// An application user account. A user belongs to exactly one tenant; the same email address
/// may exist in different tenants (uniqueness is enforced per tenant, not globally).
/// </summary>
public class User : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    public string Email { get; set; } = string.Empty;

    /// <summary>PBKDF2 hash produced by the password hasher. Never stored or logged in plain text.</summary>
    public string PasswordHash { get; set; } = string.Empty;

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTime? LastLoginDate { get; set; }

    // Navigation
    public Tenant? Tenant { get; set; }
    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
}
