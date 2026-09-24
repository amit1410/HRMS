using HRMS.Application.Services;
using HRMS.Domain.Authorization;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class SeparationExitSecurityTests
{
    [Fact]
    public async Task Employee_cannot_close_self()
    {
        using var database = new SqliteInMemoryDatabase(); var f = await SeparationExitExecutionTests.ReadyFixtureAsync(database); await using var db = database.CreateContext(new TestTenantContext(f.TenantId, f.EmployeeUserId));
        var result = await new SeparationExitService(db, new TestTenantContext(f.TenantId, f.EmployeeUserId), TimeProvider.System).ExecuteAsync(f.SeparationId, new());
        Assert.Equal(HRMS.Application.Common.ResultStatus.Forbidden, result.Status);
    }

    [Fact]
    public async Task Cross_tenant_exit_denied()
    {
        using var database = new SqliteInMemoryDatabase(); var f = await SeparationExitExecutionTests.ReadyFixtureAsync(database); await using var db = database.CreateIsolatedContext(new TestTenantContext(Guid.NewGuid(), f.HrUserId));
        var result = await new SeparationExitService(db, new TestTenantContext(Guid.NewGuid(), f.HrUserId), TimeProvider.System).GetReadinessAsync(f.SeparationId);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Closed_employee_cannot_use_normal_exit_access()
    {
        using var database = new SqliteInMemoryDatabase(); var f = await SeparationExitExecutionTests.ReadyFixtureAsync(database); await using var db = database.CreateContext(new TestTenantContext(f.TenantId, f.HrUserId));
        Assert.True((await new SeparationExitService(db, new TestTenantContext(f.TenantId, f.HrUserId), TimeProvider.System).ExecuteAsync(f.SeparationId, new())).Succeeded);
        Assert.False((await db.Users.SingleAsync(x => x.Id == f.EmployeeUserId)).IsActive);
    }

    [Fact]
    public async Task Revoked_session_cannot_refresh()
    {
        using var database = new SqliteInMemoryDatabase(); var f = await SeparationExitExecutionTests.ReadyFixtureAsync(database); await using var db = database.CreateContext(new TestTenantContext(f.TenantId, f.HrUserId));
        Assert.Equal(1, await db.AccountEmployeeCurrentLinks.CountAsync(x => x.EmployeeId == f.EmployeeId)); db.RefreshTokens.Add(new HRMS.Domain.Entities.RefreshToken { Id = Guid.NewGuid(), TenantId = f.TenantId, UserId = f.EmployeeUserId, TokenHash = "security-hash", ExpiresAtUtc = DateTime.UtcNow.AddHours(1) }); await db.SaveChangesAsync(); var result = await new SeparationExitService(db, new TestTenantContext(f.TenantId, f.HrUserId), TimeProvider.System).ExecuteAsync(f.SeparationId, new()); Assert.True(result.Succeeded, result.Message); Assert.NotNull((await db.RefreshTokens.SingleAsync()).RevokedAtUtc);
    }

    [Fact]
    public async Task Foreign_tenant_records_remain_untouched()
    {
        using var database = new SqliteInMemoryDatabase(); var f = await SeparationExitExecutionTests.ReadyFixtureAsync(database); var otherTenant = Guid.NewGuid(); await using var db = database.CreateContext(new TestTenantContext()); db.Tenants.Add(new HRMS.Domain.Entities.Tenant { Id = otherTenant, TenantCode = "OTHER", TenantName = "Other", Host = "other.test", ShardKey = "other" }); await db.SaveChangesAsync(); await using var tenantDb = database.CreateIsolatedContext(new TestTenantContext(f.TenantId, f.HrUserId)); Assert.True((await new SeparationExitService(tenantDb, new TestTenantContext(f.TenantId, f.HrUserId), TimeProvider.System).ExecuteAsync(f.SeparationId, new())).Succeeded); await using var verify = database.CreateIsolatedContext(new TestTenantContext(otherTenant)); Assert.Empty(await verify.SeparationExitExecutions.ToListAsync());
    }

    [Fact]
    public async Task Ordinary_retry_cannot_reactivate_closed_employee()
    {
        using var database = new SqliteInMemoryDatabase(); var f = await SeparationExitExecutionTests.ReadyFixtureAsync(database); await using var db = database.CreateContext(new TestTenantContext(f.TenantId, f.HrUserId)); var service = new SeparationExitService(db, new TestTenantContext(f.TenantId, f.HrUserId), TimeProvider.System); Assert.True((await service.ExecuteAsync(f.SeparationId, new())).Succeeded); Assert.True((await service.RetryAsync(f.SeparationId, new("normal retry"))).Succeeded); Assert.Equal(EmployeeStatus.Terminated, (await db.Employees.SingleAsync(x => x.Id == f.EmployeeId)).Status);
    }

    [Fact]
    public async Task Closed_separation_cannot_be_reopened_by_exit_endpoint()
    {
        using var database = new SqliteInMemoryDatabase(); var f = await SeparationExitExecutionTests.ReadyFixtureAsync(database); await using var db = database.CreateContext(new TestTenantContext(f.TenantId, f.HrUserId)); var service = new SeparationExitService(db, new TestTenantContext(f.TenantId, f.HrUserId), TimeProvider.System); Assert.True((await service.ExecuteAsync(f.SeparationId, new())).Succeeded); Assert.Equal(EmployeeSeparationStatus.Closed, (await db.EmployeeSeparations.SingleAsync(x => x.Id == f.SeparationId)).Status);
    }

    [Fact]
    public async Task Exit_execution_tables_are_tenant_scoped()
    {
        using var database = new SqliteInMemoryDatabase(); await using var db = database.CreateContext(new TestTenantContext(Guid.NewGuid())); Assert.NotNull(db.Model.FindEntityType(typeof(HRMS.Domain.Entities.Separation.SeparationExitExecution))); Assert.NotNull(db.Model.FindEntityType(typeof(HRMS.Domain.Entities.Separation.SeparationExitExecutionEvent)));
    }

    [Fact]
    public async Task Privileged_tenant_role_is_deprovisioned_without_global_history_deletion()
    {
        using var database = new SqliteInMemoryDatabase(); var f = await SeparationExitExecutionTests.ReadyFixtureAsync(database);
        await using (var setup = database.CreateContext(new TestTenantContext(f.TenantId, f.HrUserId)))
        {
            setup.Roles.Add(new Role { Id = SeedData.RoleId(RoleNames.HRBP), Name = RoleNames.HRBP, Description = "HR business partner" });
            setup.UserRoles.Add(new UserRole { Id = Guid.NewGuid(), TenantId = f.TenantId, UserId = f.EmployeeUserId, RoleId = SeedData.RoleId(RoleNames.HRBP), EffectiveFrom = DateOnly.MinValue, AssignmentSource = RoleAssignmentSource.Manual });
            await setup.SaveChangesAsync();
        }
        await using var db = database.CreateContext(new TestTenantContext(f.TenantId, f.HrUserId));
        var result = await new SeparationExitService(db, new TestTenantContext(f.TenantId, f.HrUserId), TimeProvider.System).ExecuteAsync(f.SeparationId, new());
        Assert.True(result.Succeeded, result.Message);
        var assignment = await db.UserRoles.SingleAsync(x => x.UserId == f.EmployeeUserId && x.RoleId == SeedData.RoleId(RoleNames.HRBP));
        Assert.NotNull(assignment.EffectiveTo);
        Assert.Single(await db.UserRoleAssignmentEvents.Where(x => x.AssignmentId == assignment.Id && x.EventType == UserRoleAssignmentEventType.Revoked).ToListAsync());
    }

    [Fact]
    public async Task Manager_exit_blocks_without_replacement_and_succeeds_with_replacement()
    {
        using var database = new SqliteInMemoryDatabase(); var f = await SeparationExitExecutionTests.ReadyFixtureAsync(database);
        await using var setup = database.CreateContext(new TestTenantContext(f.TenantId, f.HrUserId));
        var replacement = await setup.Employees.SingleAsync(x => x.TenantId == f.TenantId && x.Id != f.EmployeeId);
        var report = new Employee { Id = Guid.NewGuid(), TenantId = f.TenantId, EmployeeCode = "REPORT-1", FirstName = "Direct", LastName = "Report", Email = "report@test.local", DateOfJoining = new(2020, 1, 1), Status = EmployeeStatus.Active, ReportingManagerId = f.EmployeeId };
        setup.Employees.Add(report); await setup.SaveChangesAsync();
        var service = new SeparationExitService(setup, new TestTenantContext(f.TenantId, f.HrUserId), TimeProvider.System);
        var blocked = await service.GetReadinessAsync(f.SeparationId);
        Assert.Contains(blocked.Value!.Blockers, x => x.Code == "ManagerReassignmentRequired");
        report.ReportingManagerId = replacement.Id; await setup.SaveChangesAsync();
        var executed = await service.ExecuteAsync(f.SeparationId, new());
        Assert.True(executed.Succeeded, executed.Message);
        Assert.Equal(replacement.Id, (await setup.Employees.SingleAsync(x => x.Id == report.Id)).ReportingManagerId);
    }
}
