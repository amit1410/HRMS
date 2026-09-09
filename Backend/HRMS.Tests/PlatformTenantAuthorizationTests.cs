using HRMS.Domain.Authorization;
using HRMS.Application.DTOs.PlatformTenants;
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

    [Theory]
    [InlineData(nameof(PlatformTenantsController.Activate))]
    [InlineData(nameof(PlatformTenantsController.Deactivate))]
    public void Tenant_status_actions_require_the_existing_platform_status_permission(string action)
    {
        var method = typeof(PlatformTenantsController).GetMethod(action);
        var permission = method?.GetCustomAttribute<PlatformPermissionAttribute>();

        Assert.NotNull(permission);
        Assert.Equal(PlatformPermissions.TenantUpdateStatus, permission!.Policy);
    }

    [Fact]
    public void Tenant_metadata_update_contract_excludes_provisioning_identity_and_credentials()
    {
        var properties = typeof(UpdateInactivePlatformTenantRequest)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain("Id", properties);
        Assert.DoesNotContain("DatabaseProvider", properties);
        Assert.DoesNotContain("ShardKey", properties);
        Assert.DoesNotContain("ConnectionString", properties);
        Assert.DoesNotContain("Password", properties);
    }
}
