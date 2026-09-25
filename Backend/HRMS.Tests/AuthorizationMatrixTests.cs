using HRMS.Domain.Authorization;
using HRMS.Infrastructure.Persistence.Seed;

namespace HRMS.Tests;

public sealed class AuthorizationMatrixTests
{
    public static IEnumerable<object[]> CanonicalRoleMatrix() =>
    [
        [RoleNames.Employee, true, false, false, false, true, true, false],
        [RoleNames.Manager, false, true, false, false, true, true, false],
        [RoleNames.HRBP, false, true, true, false, true, true, false],
        [RoleNames.EmployeeRelationshipOfficer, false, false, true, false, false, false, false],
        [RoleNames.TimeManager, false, false, true, false, false, true, false],
        [RoleNames.IT, false, false, false, false, false, false, false],
        [RoleNames.Accounts, false, false, false, false, false, false, false],
        [RoleNames.HRAdmin, false, false, false, true, true, true, true],
        [RoleNames.SuperHR, false, true, false, false, true, true, true],
        [RoleNames.TenantAdmin, true, true, false, true, true, true, true]
    ];

    [Theory]
    [MemberData(nameof(CanonicalRoleMatrix))]
    public void Canonical_role_matrix_has_explicit_business_boundaries(
        string roleName,
        bool selfService,
        bool managerTeam,
        bool organizationalScope,
        bool tenantWide,
        bool leaveAccess,
        bool attendanceAccess,
        bool roleAdministration)
    {
        Assert.Contains(roleName, RoleNames.All);
        var grants = SeedData.RolePermissionMap[roleName];

        Assert.Equal(selfService, grants.Contains(Permissions.Leave.RequestViewOwn) || grants.Contains(Permissions.Attendance.MonthlyViewSelf));
        Assert.Equal(managerTeam, grants.Contains(Permissions.Leave.Approve) || grants.Contains(Permissions.Attendance.MonthlyViewTeam));
        Assert.Equal(organizationalScope, roleName is RoleNames.HRBP or RoleNames.EmployeeRelationshipOfficer or RoleNames.TimeManager);
        Assert.Equal(tenantWide, grants.Contains(Permissions.Leave.DashboardViewAll) || grants.Contains(Permissions.Attendance.MonthlyViewAll));
        Assert.Equal(leaveAccess, grants.Any(x => x.StartsWith("Leave", StringComparison.Ordinal)));
        Assert.Equal(attendanceAccess, grants.Any(x => x.StartsWith("Attendance.", StringComparison.Ordinal)));
        Assert.Equal(roleAdministration, grants.Contains(Permissions.RoleManagement.Manage) || grants.Contains(Permissions.PageAccess.Manage));

        // Role grants are tenant-local. Cross-tenant access is denied by the
        // tenant context/query filters and is covered by the runtime suites.
        Assert.DoesNotContain("Tenant.CrossTenant", grants);
    }

    [Fact]
    public void Employee_and_manager_roles_do_not_receive_role_administration()
    {
        Assert.DoesNotContain(Permissions.RoleManagement.Manage, SeedData.RolePermissionMap[RoleNames.Employee]);
        Assert.DoesNotContain(Permissions.RoleManagement.Manage, SeedData.RolePermissionMap[RoleNames.Manager]);
        Assert.DoesNotContain(Permissions.PageAccess.Manage, SeedData.RolePermissionMap[RoleNames.Employee]);
        Assert.DoesNotContain(Permissions.PageAccess.Manage, SeedData.RolePermissionMap[RoleNames.Manager]);
    }

    [Fact]
    public void IT_and_accounts_do_not_receive_leave_or_attendance_business_permissions()
    {
        foreach (var role in new[] { RoleNames.IT, RoleNames.Accounts })
        {
            var grants = SeedData.RolePermissionMap[role];
            Assert.DoesNotContain(grants, permission => permission.StartsWith("Leave.", StringComparison.Ordinal));
            Assert.DoesNotContain(grants, permission => permission.StartsWith("Attendance.", StringComparison.Ordinal));
        }
    }
}
