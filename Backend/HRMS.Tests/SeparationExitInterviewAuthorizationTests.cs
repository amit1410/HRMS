using HRMS.API.Controllers;
using HRMS.API.Security;
using HRMS.Domain.Authorization;

namespace HRMS.Tests;

public sealed class SeparationExitInterviewAuthorizationTests
{
    [Fact]
    public void Self_hr_and_configuration_routes_reuse_separation_permissions()
    {
        AssertPermission(nameof(SeparationExitInterviewController.Mine), Permissions.Separation.ViewSelf);
        AssertPermission(nameof(SeparationExitInterviewController.Save), Permissions.Separation.ViewSelf);
        AssertPermission(nameof(SeparationExitInterviewController.Submit), Permissions.Separation.ViewSelf);
        AssertPermission(nameof(SeparationExitInterviewController.Inbox), Permissions.Separation.HrReview);
        AssertPermission(nameof(SeparationExitInterviewController.Note), Permissions.Separation.HrReview);
        AssertPermission(nameof(SeparationExitInterviewController.Complete), Permissions.Separation.HrReview);
        AssertPermission(nameof(SeparationExitInterviewController.Reopen), Permissions.Separation.Manage);
        AssertPermission(nameof(SeparationExitInterviewController.Templates), Permissions.Separation.ClearanceConfigure);
        AssertPermission(nameof(SeparationExitInterviewController.CreateVersion), Permissions.Separation.ClearanceConfigure);
    }

    private static void AssertPermission(string methodName, string expected)
    {
        var method = typeof(SeparationExitInterviewController).GetMethod(methodName);
        var permission = method!.GetCustomAttributes(typeof(HasPermissionAttribute), false).Cast<HasPermissionAttribute>().Single();
        Assert.Equal(expected, permission.Permission);
    }
}
