using HRMS.Application.Abstractions;

namespace HRMS.Tests.TestSupport;

/// <summary>
/// Test double for <see cref="ITenantContext"/> with mutable ids, so a single test can act as
/// different tenants (or as no tenant at all) against the same database.
/// </summary>
public sealed class TestTenantContext : ITenantContext
{
    public TestTenantContext(Guid? tenantId = null, Guid? userId = null)
    {
        TenantId = tenantId;
        UserId = userId;
    }

    public Guid? TenantId { get; set; }
    public Guid? UserId { get; set; }
    public bool HasTenant => TenantId.HasValue;
}
