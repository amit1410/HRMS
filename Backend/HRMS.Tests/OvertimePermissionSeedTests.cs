using HRMS.Domain.Authorization;
using HRMS.Infrastructure.Persistence.Seed;

namespace HRMS.Tests;

public sealed class OvertimePermissionSeedTests
{
    private static readonly string[] OvertimePermissions =
    [
        Permissions.Attendance.OvertimeViewSelf,
        Permissions.Attendance.OvertimeViewTeam,
        Permissions.Attendance.OvertimeViewAll,
        Permissions.Attendance.OvertimeRequest,
        Permissions.Attendance.OvertimeApprove,
        Permissions.Attendance.OvertimeFinalize,
        Permissions.Attendance.OvertimeReopen
    ];

    [Fact]
    public void All_overtime_permissions_have_unique_stable_ids()
    {
        var ids = OvertimePermissions.Select(SeedData.PermissionId).ToArray();

        Assert.Equal(OvertimePermissions.Length, ids.Distinct().Count());
        Assert.Equal(new[] { 262, 263, 264, 265, 266, 267, 268 }, ids);
    }

    [Fact]
    public void Overtime_permissions_are_present_in_the_seeded_permission_set()
    {
        var seeded = SeedData.Permissions.Select(x => x.Name).ToHashSet(StringComparer.Ordinal);

        Assert.All(OvertimePermissions, permission => Assert.Contains(permission, seeded));
    }

    [Fact]
    public void Permission_seed_projection_is_idempotent()
    {
        var first = SeedData.Permissions
            .Where(x => OvertimePermissions.Contains(x.Name, StringComparer.Ordinal))
            .Select(x => (x.Id, x.Name))
            .OrderBy(x => x.Id)
            .ToArray();
        var second = SeedData.Permissions
            .Where(x => OvertimePermissions.Contains(x.Name, StringComparer.Ordinal))
            .Select(x => (x.Id, x.Name))
            .OrderBy(x => x.Id)
            .ToArray();

        Assert.Equal(first, second);
    }
}
