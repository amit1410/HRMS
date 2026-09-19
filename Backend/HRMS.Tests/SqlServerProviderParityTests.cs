using HRMS.Application.Services;
using HRMS.Domain.Authorization;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

/// <summary>Small SQL Server-backed parity checks for provider-sensitive authorization and attendance persistence.</summary>
public sealed class SqlServerProviderParityTests : IClassFixture<SqlServerLeaveRequestConcurrencyFixture>
{
    private readonly SqlServerLeaveRequestConcurrencyFixture _fixture;

    public SqlServerProviderParityTests(SqlServerLeaveRequestConcurrencyFixture fixture) => _fixture = fixture;

    [SqlServerProviderParityFact]
    public async Task Attendance_rows_persist_and_remain_tenant_scoped()
    {
        var date = new DateOnly(2026, 10, 21);
        await using (var tenantA = _fixture.CreateContext(_fixture.TenantA))
        {
            tenantA.AttendancePunches.Add(new AttendancePunch
            {
                Id = Guid.NewGuid(),
                TenantId = _fixture.TenantA,
                EmployeeId = _fixture.EmployeeA,
                PunchAtUtc = new DateTime(2026, 10, 21, 9, 0, 0, DateTimeKind.Utc),
                BusinessDate = date,
                Direction = PunchDirection.In,
                Source = PunchSource.Portal,
                ExternalPunchId = $"sqlserver-parity-{Guid.NewGuid():N}",
                CapturedAtUtc = DateTime.UtcNow
            });
            await tenantA.SaveChangesAsync();
        }

        await using var tenantARead = _fixture.CreateContext(_fixture.TenantA);
        Assert.Equal(1, await tenantARead.AttendancePunches.CountAsync(x => x.EmployeeId == _fixture.EmployeeA && x.BusinessDate == date));

        await using var tenantBRead = _fixture.CreateContext(_fixture.TenantB);
        Assert.Empty(await tenantBRead.AttendancePunches.Where(x => x.EmployeeId == _fixture.EmployeeA).ToListAsync());
    }

    [SqlServerProviderParityFact]
    public async Task Effective_role_resolution_is_tenant_scoped_on_sql_server()
    {
        var roleId = Random.Shared.Next(700_000, 800_000);
        await using (var setup = _fixture.CreateContext(_fixture.TenantA))
        {
            setup.Roles.Add(new Role { Id = roleId, Name = $"SQL Server parity role {roleId}" });
            setup.UserRoles.Add(new UserRole
            {
                Id = Guid.NewGuid(),
                TenantId = _fixture.TenantA,
                UserId = _fixture.UserA,
                RoleId = roleId,
                EffectiveFrom = new DateOnly(2026, 1, 1)
            });
            await setup.SaveChangesAsync();
        }

        await using var tenantARead = _fixture.CreateContext(_fixture.TenantA);
        var roles = await new RoleResolutionService(tenantARead).GetEffectiveRoleIdsAsync(_fixture.TenantA, _fixture.UserA, new(2026, 10, 21));
        Assert.Contains(roleId, roles);

        await using var tenantBRead = _fixture.CreateContext(_fixture.TenantB);
        var crossTenantRoles = await new RoleResolutionService(tenantBRead).GetEffectiveRoleIdsAsync(_fixture.TenantB, _fixture.UserA, new(2026, 10, 21));
        Assert.DoesNotContain(roleId, crossTenantRoles);
    }

    [SqlServerProviderParityFact]
    public async Task Page_access_update_persists_permission_changes_and_history()
    {
        var roleId = Random.Shared.Next(800_000, 900_000);
        var permissionId = Random.Shared.Next(700_000, 800_000);
        await using var setup = _fixture.CreateContext(_fixture.TenantA, _fixture.UserA);
        setup.Roles.Add(new Role { Id = roleId, Name = $"SQL Server page role {roleId}" });
        setup.Permissions.Add(new Permission { Id = permissionId, Name = Permissions.PageAccess.View });
        await setup.SaveChangesAsync();

        var tenant = new TestTenantContext(_fixture.TenantA, _fixture.UserA);
        var service = new PageAccessService(setup, tenant, new RoleResolutionService(setup), TimeProvider.System);
        var updated = await service.UpdateAsync(roleId, new([Permissions.PageAccess.View], "SQL Server parity"));

        Assert.True(updated.Succeeded);
        Assert.Contains(Permissions.PageAccess.View, updated.Value!.GrantedPermissions);
        Assert.Single(await setup.AuthorizationConfigurationEvents.Where(x => x.RoleId == roleId && x.PermissionCode == Permissions.PageAccess.View).ToListAsync());

        var matrix = await service.GetMatrixAsync(roleId);
        Assert.True(matrix.Succeeded);
        Assert.Contains(Permissions.PageAccess.View, matrix.Value!.GrantedPermissions);
    }

}

public sealed class SqlServerProviderParityFactAttribute : FactAttribute
{
    public SqlServerProviderParityFactAttribute() => Skip = SqlServerLeaveRequestConcurrencyFixture.IsConfigured
        ? null
        : $"SQL Server provider parity tests not executed: {SqlServerAcceptanceRun.ConnectionEnvironmentVariable} is absent.";
}
