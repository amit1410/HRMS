using HRMS.Domain.Authorization;

namespace HRMS.Tests;

public sealed class PlatformTenantAuthorizationTests
{
    [Fact]
    public void Platform_permissions_are_granted_only_to_super_admin_seed_role()
    {
        Assert.Contains(PlatformPermissions.TenantView, PlatformPermissions.All);
        Assert.Contains(PlatformPermissions.TenantCreate, PlatformPermissions.All);
        Assert.Contains(PlatformPermissions.TenantUpdateStatus, PlatformPermissions.All);
        Assert.DoesNotContain(PlatformPermissions.TenantCreate, Permissions.All);
    }
}
