using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Roles;
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
    public async Task Attendance_self_scope_requires_authoritative_link_and_effective_permission()
    {
        var roleId = Random.Shared.Next(600_000, 700_000);
        var permissionId = Random.Shared.Next(600_000, 700_000);
        await using var setup = _fixture.CreateContext(_fixture.TenantA, _fixture.UserA);
        setup.Roles.Add(new Role { Id = roleId, Name = $"SQL Server attendance role {roleId}" });
        setup.Permissions.Add(new Permission { Id = permissionId, Name = Permissions.Attendance.View });
        setup.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionId = permissionId });
        setup.UserRoles.Add(new UserRole { Id = Guid.NewGuid(), TenantId = _fixture.TenantA, UserId = _fixture.UserA, RoleId = roleId, EffectiveFrom = new(2026, 1, 1) });
        var linkId = Guid.NewGuid();
        setup.AccountEmployeeLinkEvents.Add(new AccountEmployeeLinkEvent
        {
            Id = linkId,
            TenantId = _fixture.TenantA,
            SubjectUserId = _fixture.UserA,
            ActorUserId = _fixture.UserA,
            Sequence = 1,
            Operation = "Link",
            NewLinkId = linkId,
            AfterEmployeeId = _fixture.EmployeeA,
            OccurredAtUtc = DateTime.UtcNow,
            Reason = "SQL Server parity self-service link",
            CorrelationId = Guid.NewGuid().ToString("N")
        });
        setup.AccountEmployeeCurrentLinks.Add(new AccountEmployeeCurrentLink { LinkId = linkId, TenantId = _fixture.TenantA, UserId = _fixture.UserA, EmployeeId = _fixture.EmployeeA });
        await setup.SaveChangesAsync();

        var tenant = new TestTenantContext(_fixture.TenantA, _fixture.UserA);
        var authorization = new AttendanceAuthorizationService(
            setup,
            tenant,
            new EmployeeIdentityResolver(setup, tenant),
            new EmployeeAccessScopeService(setup, tenant),
            new EmployeeManagerResolver(setup, tenant),
            new NoopAuthorizationContext());

        var allowed = await authorization.CanAccessEmployeeAsync(_fixture.EmployeeA, Permissions.Attendance.View, true, false, false, new(2026, 10, 21));
        var unrelated = await authorization.CanAccessEmployeeAsync(_fixture.EmployeeB, Permissions.Attendance.View, true, false, false, new(2026, 10, 21));
        Assert.True(allowed.Succeeded && allowed.Value);
        Assert.True(unrelated.Succeeded && !unrelated.Value);
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
    public async Task Role_assignment_and_revocation_persist_tenant_scoped_history()
    {
        var roleId = Random.Shared.Next(500_000, 600_000);
        await using var setup = _fixture.CreateContext(_fixture.TenantA, _fixture.UserA);
        setup.Roles.Add(new Role { Id = roleId, Name = $"SQL Server assignable role {roleId}" });
        await setup.SaveChangesAsync();

        var tenant = new TestTenantContext(_fixture.TenantA, _fixture.UserA);
        var service = new RoleAssignmentService(setup, tenant, TimeProvider.System);
        var assigned = await service.AssignAsync(_fixture.UserB, new(roleId, new(2026, 1, 1), null, "SQL Server parity", null));
        Assert.True(assigned.Succeeded);

        var revoked = await service.RevokeAsync(assigned.Value!.AssignmentId, new(new(2026, 10, 21), "SQL Server parity revoke"));
        Assert.True(revoked.Succeeded);
        Assert.Equal(new DateOnly(2026, 10, 21), revoked.Value!.EffectiveTo);

        var history = await service.GetAssignmentHistoryAsync(assigned.Value.AssignmentId);
        Assert.True(history.Succeeded);
        Assert.Contains(history.Value!, x => x.EventType == UserRoleAssignmentEventType.Assigned);
        Assert.Contains(history.Value!, x => x.EventType == UserRoleAssignmentEventType.Revoked);

        await using var tenantB = _fixture.CreateContext(_fixture.TenantB, _fixture.UserC);
        var crossTenant = await new RoleAssignmentService(tenantB, new TestTenantContext(_fixture.TenantB, _fixture.UserC), TimeProvider.System)
            .GetUserAssignmentsAsync(_fixture.UserB);
        Assert.Equal(ResultStatus.NotFound, crossTenant.Status);
    }

    [SqlServerProviderParityFact]
    public async Task Page_access_update_persists_permission_changes_and_history()
    {
        var roleId = Random.Shared.Next(800_000, 900_000);
        var permissionId = Random.Shared.Next(700_000, 800_000);
        await using var setup = _fixture.CreateContext(_fixture.TenantA, _fixture.UserA);
        setup.Roles.Add(new Role { Id = roleId, Name = $"SQL Server page role {roleId}" });
        if (!await setup.Permissions.AnyAsync(x => x.Name == Permissions.PageAccess.View))
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

        var revoked = await service.UpdateAsync(roleId, new([], "SQL Server parity revoke"));
        Assert.True(revoked.Succeeded);
        Assert.DoesNotContain(Permissions.PageAccess.View, revoked.Value!.GrantedPermissions);
    }

    private sealed class NoopAuthorizationContext : ICurrentAuthorizationContext
    {
        public bool HasAnyPermission(params string[] permissions) => false;
    }
}

public sealed class SqlServerProviderParityFactAttribute : FactAttribute
{
    public SqlServerProviderParityFactAttribute() => Skip = SqlServerLeaveRequestConcurrencyFixture.IsConfigured
        ? null
        : $"SQL Server provider parity tests not executed: {SqlServerAcceptanceRun.ConnectionEnvironmentVariable} is absent.";
}
