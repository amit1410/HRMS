using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Entities.Separation;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SeparationReasonEntity = HRMS.Domain.Entities.Separation.SeparationReason;

namespace HRMS.Tests;

public sealed class SeparationApprovalTests
{
    [Fact]
    public async Task HrApproval_notice_sync_failure_rolls_back_all_changes_and_retry_succeeds()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid(); var employeeId = Guid.NewGuid(); var managerId = Guid.NewGuid(); var employeeUser = Guid.NewGuid(); var managerUser = Guid.NewGuid(); var hrUser = Guid.NewGuid();
        await SeedTenantAsync(database, tenantId); await SeedEmployeeAsync(database, tenantId, managerId, managerUser, null, true); await SeedEmployeeAsync(database, tenantId, employeeId, employeeUser, managerId, true); await SeedEmploymentAsync(database, tenantId, employeeId, 30); await SeedManagerHistoryAsync(database, tenantId, employeeId, managerId);
        await using (var seed = database.CreateContext(new TestTenantContext(tenantId, employeeUser)))
        {
            var reason = new SeparationReasonEntity { Id = Guid.NewGuid(), TenantId = tenantId, Code = "ROLLBACK", Name = "Rollback test", Category = SeparationReasonCategory.Resignation, EmployeeInitiatedAllowed = true, EffectiveFrom = new(2020, 1, 1), IsActive = true }; seed.SeparationReasons.Add(reason); await seed.SaveChangesAsync();
            var employeeTenant = new TestTenantContext(tenantId, employeeUser); var created = await CreateService(seed, employeeTenant).CreateSelfAsync(new(reason.Id, new(2026, 9, 23), new(2026, 10, 23), null)); Assert.True(created.Succeeded); await CreateService(seed, employeeTenant).SubmitAsync(created.Value!.Id);
            var managerTenant = new TestTenantContext(tenantId, managerUser); Assert.True((await CreateService(seed, managerTenant).ManagerApproveAsync(created.Value.Id)).Succeeded);
            var failingTenant = new TestTenantContext(tenantId, hrUser);
            await using (var failing = database.CreateContext(failingTenant, new ThrowOnceOnSaveInterceptor()))
            {
                var failed = await CreateService(failing, failingTenant).HrApproveAsync(created.Value.Id);
                Assert.False(failed.Succeeded);
            }
            await using var fresh = database.CreateContext(new TestTenantContext(tenantId, hrUser));
            var afterFailure = await fresh.EmployeeSeparations.SingleAsync(x => x.Id == created.Value.Id);
            var employmentAfterFailure = await fresh.EmployeeEmployments.SingleAsync(x => x.EmployeeId == employeeId);
            Assert.NotEqual(EmployeeSeparationStatus.Approved, afterFailure.Status); Assert.Null(afterFailure.ApprovedLastWorkingDate); Assert.Equal(NoticePeriodStatus.NotServing, employmentAfterFailure.NoticeStatus);
            Assert.Equal(0, await fresh.EmployeeSeparationEvents.CountAsync(x => x.EventType == EmployeeSeparationEventType.HrApproved));
            var retried = await CreateService(fresh, new TestTenantContext(tenantId, hrUser)).HrApproveAsync(created.Value.Id);
            Assert.True(retried.Succeeded); Assert.Equal(EmployeeSeparationStatus.Approved, retried.Value!.Status);
            Assert.Equal(1, await fresh.EmployeeSeparationEvents.CountAsync(x => x.EventType == EmployeeSeparationEventType.HrApproved));
            Assert.Equal(1, await fresh.EmployeeSeparationEvents.CountAsync(x => x.EventType == EmployeeSeparationEventType.NoticePeriodActivated));
        }
    }
    [Fact]
    public async Task Manager_and_hr_approval_sets_authoritative_lwd_and_notice_fields_without_exit_or_payroll_side_effects()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid(); var employeeId = Guid.NewGuid(); var managerId = Guid.NewGuid();
        var employeeUser = Guid.NewGuid(); var managerUser = Guid.NewGuid(); var hrUser = Guid.NewGuid();
        await SeedTenantAsync(database, tenantId);
        await SeedEmployeeAsync(database, tenantId, managerId, managerUser, null, true);
        await SeedEmployeeAsync(database, tenantId, employeeId, employeeUser, managerId, true);
        await SeedEmploymentAsync(database, tenantId, employeeId, 30);
        await SeedManagerHistoryAsync(database, tenantId, employeeId, managerId);
        var employeeTenant = new TestTenantContext(tenantId, employeeUser);
        await using var employeeDb = database.CreateContext(employeeTenant);
        var reason = new SeparationReasonEntity { Id = Guid.NewGuid(), TenantId = tenantId, Code = "VOL", Name = "Voluntary", Category = SeparationReasonCategory.Resignation, EmployeeInitiatedAllowed = true, EffectiveFrom = new(2020, 1, 1), IsActive = true };
        employeeDb.SeparationReasons.Add(reason); await employeeDb.SaveChangesAsync();
        var employeeService = CreateService(employeeDb, employeeTenant);
        var created = await employeeService.CreateSelfAsync(new(reason.Id, new(2026, 9, 23), new(2026, 10, 23), "Moving on"));
        Assert.True(created.Succeeded); Assert.True((await employeeService.SubmitAsync(created.Value!.Id)).Succeeded);

        var managerTenant = new TestTenantContext(tenantId, managerUser);
        await using var managerDb = database.CreateContext(managerTenant);
        var managerService = CreateService(managerDb, managerTenant);
        var managerApproved = await managerService.ManagerApproveAsync(created.Value.Id);
        Assert.True(managerApproved.Succeeded); Assert.Equal(EmployeeSeparationStatus.HrReview, managerApproved.Value!.Status);

        var hrTenant = new TestTenantContext(tenantId, hrUser);
        await using var hrDb = database.CreateContext(hrTenant);
        var approved = await CreateService(hrDb, hrTenant).HrApproveAsync(created.Value.Id);
        Assert.True(approved.Succeeded); Assert.Equal(EmployeeSeparationStatus.Approved, approved.Value!.Status);
        Assert.Equal(new(2026, 10, 23), approved.Value.ApprovedLastWorkingDate);
        var employment = await hrDb.EmployeeEmployments.SingleAsync(x => x.EmployeeId == employeeId);
        Assert.Equal(NoticePeriodStatus.Active, employment.NoticeStatus);
        Assert.Equal(new(2026, 9, 23), employment.NoticeStartDate);
        Assert.Equal(new(2026, 10, 23), employment.NoticeEndDate);
        Assert.Null(await hrDb.Employees.Where(x => x.Id == employeeId).Select(x => x.DateOfLeaving).SingleAsync());
        Assert.Equal(5, await hrDb.EmployeeSeparationEvents.CountAsync(x => x.EmployeeSeparationId == created.Value.Id));
    }

    [Fact]
    public async Task Manager_rejection_and_post_approval_lwd_change_are_blocked_and_audited()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid(); var employeeId = Guid.NewGuid(); var managerId = Guid.NewGuid(); var employeeUser = Guid.NewGuid(); var managerUser = Guid.NewGuid();
        await SeedTenantAsync(database, tenantId); await SeedEmployeeAsync(database, tenantId, managerId, managerUser, null, true); await SeedEmployeeAsync(database, tenantId, employeeId, employeeUser, managerId, true); await SeedEmploymentAsync(database, tenantId, employeeId, 30); await SeedManagerHistoryAsync(database, tenantId, employeeId, managerId);
        await using var seed = database.CreateContext(new TestTenantContext(tenantId, employeeUser));
        var reason = new SeparationReasonEntity { Id = Guid.NewGuid(), TenantId = tenantId, Code = "VOL2", Name = "Voluntary", Category = SeparationReasonCategory.Resignation, EmployeeInitiatedAllowed = true, EffectiveFrom = new(2020, 1, 1), IsActive = true }; seed.SeparationReasons.Add(reason); await seed.SaveChangesAsync();
        var employeeTenant = new TestTenantContext(tenantId, employeeUser); await using var employeeDb = database.CreateContext(employeeTenant); var service = CreateService(employeeDb, employeeTenant); var created = await service.CreateSelfAsync(new(reason.Id, new(2026, 9, 23), new(2026, 10, 23), null)); Assert.True(created.Succeeded); await service.SubmitAsync(created.Value!.Id);
        var managerTenant = new TestTenantContext(tenantId, managerUser); await using var managerDb = database.CreateContext(managerTenant); var managerService = CreateService(managerDb, managerTenant); Assert.True((await managerService.ManagerApproveAsync(created.Value.Id)).Succeeded);
        var hrTenant = new TestTenantContext(tenantId, Guid.NewGuid()); await using var hrDb = database.CreateContext(hrTenant); var hrService = CreateService(hrDb, hrTenant); Assert.True((await hrService.HrApproveAsync(created.Value.Id)).Succeeded);
        var revised = await hrService.ReviseLwdAsync(created.Value.Id, new(new(2026, 11, 1), "Post approval change"));
        Assert.False(revised.Succeeded); Assert.Equal(HRMS.Application.Common.ResultStatus.Conflict, revised.Status);
        Assert.Equal(1, await hrDb.EmployeeSeparationEvents.CountAsync(x => x.EventType == EmployeeSeparationEventType.HrApproved));
    }

    private static SeparationService CreateService(HRMS.Infrastructure.Persistence.HrmsDbContext db, TestTenantContext tenant) => new(db, tenant, new EmployeeIdentityResolver(db, tenant), new EmployeeManagerResolver(db, tenant), TimeProvider.System);

    private static async Task SeedTenantAsync(SqliteInMemoryDatabase database, Guid tenantId) { await using var db = database.CreateContext(new TestTenantContext()); db.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"T{tenantId:N}"[..20], Host = $"{tenantId:N}.test", ShardKey = tenantId.ToString("N") }); await db.SaveChangesAsync(); }
    private static async Task SeedEmployeeAsync(SqliteInMemoryDatabase database, Guid tenantId, Guid employeeId, Guid userId, Guid? managerId, bool linked) { await using var db = database.CreateContext(new TestTenantContext(tenantId, userId)); db.Employees.Add(new Employee { Id = employeeId, TenantId = tenantId, EmployeeCode = $"E-{employeeId:N}"[..12], FirstName = "Test", LastName = "Employee", Email = $"{employeeId:N}@test.local", DateOfJoining = new(2025, 1, 1), ReportingManagerId = managerId }); if (linked) { db.Users.Add(new User { Id = userId, TenantId = tenantId, Email = $"{userId:N}@test.local", PasswordHash = "test-hash", FirstName = "Test", LastName = "User", IsActive = true }); var linkId = Guid.NewGuid(); db.AccountEmployeeLinkEvents.Add(new AccountEmployeeLinkEvent { Id = linkId, TenantId = tenantId, SubjectUserId = userId, ActorUserId = userId, Sequence = 1, Operation = "Link", NewLinkId = linkId, AfterEmployeeId = employeeId, OccurredAtUtc = DateTime.UtcNow, Reason = "test", CorrelationId = linkId.ToString("N") }); db.AccountEmployeeCurrentLinks.Add(new AccountEmployeeCurrentLink { LinkId = linkId, TenantId = tenantId, UserId = userId, EmployeeId = employeeId }); } await db.SaveChangesAsync(); }
    private static async Task SeedEmploymentAsync(SqliteInMemoryDatabase database, Guid tenantId, Guid employeeId, int noticePeriod) { await using var db = database.CreateContext(new TestTenantContext(tenantId)); db.EmployeeEmployments.Add(new EmployeeEmployment { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId, FirstHiredDate = new(2025, 1, 1), DateOfJoining = new(2025, 1, 1), NoticePeriod = noticePeriod, NoticePeriodUnit = "Days" }); await db.SaveChangesAsync(); }
    private static async Task SeedManagerHistoryAsync(SqliteInMemoryDatabase database, Guid tenantId, Guid employeeId, Guid managerId) { await using var db = database.CreateContext(new TestTenantContext(tenantId)); db.EmployeeEmploymentHistory.Add(new EmployeeEmploymentHistory { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId, EffectiveFrom = new(2020, 1, 1), ManagerId = managerId, EmploymentStatus = EmployeeStatus.Active }); db.EmployeeEmploymentHistory.Add(new EmployeeEmploymentHistory { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = managerId, EffectiveFrom = new(2020, 1, 1), EmploymentStatus = EmployeeStatus.Active }); await db.SaveChangesAsync(); }
}

internal sealed class ThrowOnceOnSaveInterceptor : SaveChangesInterceptor
{
    private bool thrown;
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (!thrown) { thrown = true; throw new DbUpdateException("Deterministic approval save failure."); }
        return ValueTask.FromResult(result);
    }
}
