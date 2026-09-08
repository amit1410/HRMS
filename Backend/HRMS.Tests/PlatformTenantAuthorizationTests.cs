using HRMS.Domain.Authorization;
using HRMS.API.Controllers;
using HRMS.API.Security;
using System.Reflection;

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

    [Fact]
    public void Password_reset_uses_existing_platform_tenant_create_permission()
    {
        var method = typeof(PlatformTenantsController).GetMethod(nameof(PlatformTenantsController.ResetAdminPassword));
        var permission = method?.GetCustomAttribute<PlatformPermissionAttribute>();

        Assert.NotNull(permission);
        Assert.Equal(PlatformPermissions.TenantCreate, permission!.Policy);
    }
}
