namespace HRMS.Application.Abstractions;

/// <summary>
/// Provides the tenant/user identity for the current request, resolved server-side from the
/// authenticated principal's JWT claims (never from client-supplied input). The EF Core
/// <c>HrmsDbContext</c> consumes this to apply tenant global query filters and to stamp TenantId
/// on new rows.
/// </summary>
public interface ITenantContext
{
    /// <summary>The current tenant id, or null when no authenticated tenant is resolved (e.g. login, seeding).</summary>
    Guid? TenantId { get; }

    /// <summary>The current user id, or null when unauthenticated.</summary>
    Guid? UserId { get; }

    /// <summary>True when a tenant has been resolved for the current request.</summary>
    bool HasTenant { get; }
}
