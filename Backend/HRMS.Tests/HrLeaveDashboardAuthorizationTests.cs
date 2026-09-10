using HRMS.API.Controllers;
using HRMS.API.Security;
using HRMS.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace HRMS.Tests;

public sealed class HrLeaveDashboardAuthorizationTests
{
    [Fact]
    public void Summary_endpoint_requires_the_dedicated_view_all_permission()
    {
        var method = typeof(HrLeaveDashboardController).GetMethod(nameof(HrLeaveDashboardController.GetSummary));
        var permission = method!.GetCustomAttributes(typeof(HasPermissionAttribute), false).Cast<HasPermissionAttribute>().Single();
        Assert.Equal(Permissions.Leave.DashboardViewAll, permission.Permission);
    }
}
