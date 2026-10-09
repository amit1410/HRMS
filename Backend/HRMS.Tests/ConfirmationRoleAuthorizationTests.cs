using HRMS.Domain.Authorization;
using HRMS.Infrastructure.Persistence.Seed;

namespace HRMS.Tests;

public sealed class ConfirmationRoleAuthorizationTests
{
    [Fact]
    public void Hrbp_can_create_and_decide_confirmation_cases_without_tenant_admin()
    {
        var permissions = SeedData.RolePermissionMap[RoleNames.HRBP];

        Assert.Contains(Permissions.Confirmation.PolicyView, permissions);
        Assert.Contains(Permissions.Confirmation.ReviewCaseView, permissions);
        Assert.Contains(Permissions.Confirmation.ReviewCaseManage, permissions);
        Assert.Contains(Permissions.Confirmation.ReviewHrbpSubmit, permissions);
        Assert.Contains(Permissions.Confirmation.Decide, permissions);
    }

    [Fact]
    public void Employee_role_cannot_create_or_decide_confirmation_cases()
    {
        var permissions = SeedData.RolePermissionMap[RoleNames.Employee];

        Assert.DoesNotContain(Permissions.Confirmation.ReviewCaseManage, permissions);
        Assert.DoesNotContain(Permissions.Confirmation.ReviewManagerSubmit, permissions);
        Assert.DoesNotContain(Permissions.Confirmation.ReviewHrbpSubmit, permissions);
        Assert.DoesNotContain(Permissions.Confirmation.Decide, permissions);
    }
}
