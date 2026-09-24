using HRMS.Application.Common;
using HRMS.Application.DTOs.Separation;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Entities.Separation;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace HRMS.Tests;

public sealed class SeparationNoticeTests
{
    [Fact]
    public async Task Notice_calculation_supports_exact_shortfall_extension_and_early_release()
    {
        using var database = new SqliteInMemoryDatabase();
        var fixture = await NoticeTestData.CreateApprovedAsync(database, new(2026, 10, 2));
        await using var db = database.CreateContext(new TestTenantContext(fixture.TenantId, fixture.HrUserId));
        var service = NoticeTestData.CreateService(db, fixture.TenantId, fixture.HrUserId);
        var initial = await service.GetNoticeAsync(fixture.SeparationId);
        Assert.True(initial.Succeeded, initial.Message); Assert.Equal(30, initial.Value!.RequiredNoticeDays); Assert.Equal(new(2026, 10, 22), initial.Value.ExpectedNoticeEndDate); Assert.Equal(10, initial.Value.ServedNoticeDays); Assert.Equal(20, initial.Value.ShortfallDays); Assert.Equal(0, initial.Value.ExtensionDays);
        var extended = await service.ReviseApprovedLwdAsync(fixture.SeparationId, new(new(2026, 11, 1), "Business handover extension"));
        Assert.True(extended.Succeeded, extended.Message); Assert.Equal(40, extended.Value!.ServedNoticeDays); Assert.Equal(0, extended.Value.ShortfallDays); Assert.Equal(10, extended.Value.ExtensionDays);
        var early = await service.ReviseApprovedLwdAsync(fixture.SeparationId, new(new(2026, 10, 1), "Early release"));
        Assert.True(early.Succeeded, early.Message); Assert.Equal(9, early.Value!.ServedNoticeDays); Assert.Equal(21, early.Value.ShortfallDays); Assert.Equal(0, early.Value.ExtensionDays);
    }

    [Fact]
    public async Task Partial_and_full_waiver_are_quantity_only_and_append_history()
    {
        using var database = new SqliteInMemoryDatabase();
        var fixture = await NoticeTestData.CreateApprovedAsync(database, new(2026, 10, 2));
        await using var db = database.CreateContext(new TestTenantContext(fixture.TenantId, fixture.HrUserId));
        var service = NoticeTestData.CreateService(db, fixture.TenantId, fixture.HrUserId);
        var partial = await service.ApplyNoticeWaiverAsync(fixture.SeparationId, new(5, "Approved partial waiver"));
        Assert.True(partial.Succeeded, partial.Message); Assert.Equal(5, partial.Value!.WaivedNoticeDays); Assert.Equal(15, partial.Value.ShortfallDays);
        var full = await service.ApplyNoticeWaiverAsync(fixture.SeparationId, new(15, "Approved remaining waiver"));
        Assert.True(full.Succeeded, full.Message); Assert.Equal(20, full.Value!.WaivedNoticeDays); Assert.Equal(0, full.Value.ShortfallDays);
        var excessive = await service.ApplyNoticeWaiverAsync(fixture.SeparationId, new(1, "Excess waiver"));
        Assert.False(excessive.Succeeded); Assert.Equal(ResultStatus.ValidationFailed, excessive.Status);
        Assert.Equal(2, await db.EmployeeSeparationEvents.CountAsync(x => x.EmployeeSeparationId == fixture.SeparationId && (x.EventType == EmployeeSeparationEventType.NoticeWaiverApplied || x.EventType == EmployeeSeparationEventType.NoticeWaiverRevised)));
        Assert.Empty(await db.PayrollResults.Where(x => x.EmployeeId == fixture.EmployeeId).ToListAsync()); Assert.Empty(await db.PayrollAdjustments.Where(x => x.EmployeeId == fixture.EmployeeId).ToListAsync()); Assert.Empty(await db.FinalSettlementCases.Where(x => x.EmployeeId == fixture.EmployeeId).ToListAsync());
    }

    [Fact]
    public async Task Approved_lwd_revision_synchronizes_employment_and_rejects_invalid_states()
    {
        using var database = new SqliteInMemoryDatabase();
        var fixture = await NoticeTestData.CreateApprovedAsync(database, new(2026, 10, 2));
        await using var db = database.CreateContext(new TestTenantContext(fixture.TenantId, fixture.HrUserId));
        var service = NoticeTestData.CreateService(db, fixture.TenantId, fixture.HrUserId);
        var missingReason = await service.ReviseApprovedLwdAsync(fixture.SeparationId, new(new(2026, 10, 3), "")); Assert.False(missingReason.Succeeded);
        var beforeStart = await service.ReviseApprovedLwdAsync(fixture.SeparationId, new(new(2026, 9, 22), "Invalid date")); Assert.False(beforeStart.Succeeded);
        var revised = await service.ReviseApprovedLwdAsync(fixture.SeparationId, new(new(2026, 10, 5), "Confirmed early release")); Assert.True(revised.Succeeded, revised.Message);
        var employment = await db.EmployeeEmployments.SingleAsync(x => x.EmployeeId == fixture.EmployeeId); Assert.Equal(new(2026, 10, 5), employment.NoticeEndDate); Assert.Equal(NoticePeriodStatus.Active, employment.NoticeStatus);
        Assert.Contains(await db.EmployeeSeparationEvents.Where(x => x.EmployeeSeparationId == fixture.SeparationId).ToListAsync(), x => x.EventType == EmployeeSeparationEventType.ApprovedLwdRevised || x.EventType == EmployeeSeparationEventType.ApprovedLwdExtended || x.EventType == EmployeeSeparationEventType.ApprovedLwdReduced);
    }

    [Fact]
    public async Task Lwd_revision_employment_sync_failure_rolls_back_and_retry_succeeds()
    {
        using var database = new SqliteInMemoryDatabase();
        var fixture = await NoticeTestData.CreateApprovedAsync(database, new(2026, 10, 2));
        await using (var failing = database.CreateContext(new TestTenantContext(fixture.TenantId, fixture.HrUserId), new ThrowOnceOnNoticeSaveInterceptor()))
        {
            var failed = await NoticeTestData.CreateService(failing, fixture.TenantId, fixture.HrUserId).ReviseApprovedLwdAsync(fixture.SeparationId, new(new(2026, 10, 10), "Injected failure")); Assert.False(failed.Succeeded);
        }
        await using var fresh = database.CreateContext(new TestTenantContext(fixture.TenantId, fixture.HrUserId));
        var unchanged = await fresh.EmployeeSeparations.SingleAsync(x => x.Id == fixture.SeparationId); var unchangedEmployment = await fresh.EmployeeEmployments.SingleAsync(x => x.EmployeeId == fixture.EmployeeId);
        Assert.Equal(new(2026, 10, 2), unchanged.ApprovedLastWorkingDate); Assert.Equal(new(2026, 10, 2), unchangedEmployment.NoticeEndDate); Assert.Equal(0, await fresh.EmployeeSeparationEvents.CountAsync(x => x.EmployeeSeparationId == fixture.SeparationId && (x.EventType == EmployeeSeparationEventType.ApprovedLwdRevised || x.EventType == EmployeeSeparationEventType.ApprovedLwdExtended || x.EventType == EmployeeSeparationEventType.ApprovedLwdReduced)));
        var retried = await NoticeTestData.CreateService(fresh, fixture.TenantId, fixture.HrUserId).ReviseApprovedLwdAsync(fixture.SeparationId, new(new(2026, 10, 10), "Retry after rollback")); Assert.True(retried.Succeeded, retried.Message); Assert.Equal(new(2026, 10, 10), retried.Value!.ApprovedLastWorkingDate);
    }
}

internal sealed class ThrowOnceOnNoticeSaveInterceptor : SaveChangesInterceptor
{
    private bool thrown;
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (!thrown) { thrown = true; throw new DbUpdateException("Deterministic notice synchronization failure."); }
        return ValueTask.FromResult(result);
    }
}

internal sealed record NoticeFixture(Guid TenantId, Guid EmployeeId, Guid EmployeeUserId, Guid ManagerUserId, Guid HrUserId, Guid SeparationId);

internal static class NoticeTestData
{
    public static SeparationService CreateService(HrmsDbContext db, Guid tenantId, Guid userId) => new(db, new TestTenantContext(tenantId, userId), new EmployeeIdentityResolver(db, new TestTenantContext(tenantId, userId)), new EmployeeManagerResolver(db, new TestTenantContext(tenantId, userId)), TimeProvider.System);

    public static Task<NoticeFixture> CreateApprovedAsync(SqliteInMemoryDatabase database, DateOnly proposedLwd) => CreateApprovedAsync(context => database.CreateContext(context), proposedLwd);

    public static async Task<NoticeFixture> CreateApprovedAsync(Func<TestTenantContext, HrmsDbContext> createContext, DateOnly proposedLwd)
    {
        var tenantId = Guid.NewGuid(); var employeeId = Guid.NewGuid(); var managerId = Guid.NewGuid(); var employeeUserId = Guid.NewGuid(); var managerUserId = Guid.NewGuid(); var hrUserId = Guid.NewGuid();
        await using (var seed = createContext(new TestTenantContext(tenantId)))
        {
            seed.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"N{tenantId:N}"[..12], Host = $"{tenantId:N}.notice.test", ShardKey = tenantId.ToString("N") });
            seed.Users.AddRange(new User { Id = employeeUserId, TenantId = tenantId, Email = $"{employeeUserId:N}@test.local", PasswordHash = "test-hash", FirstName = "Notice", LastName = "Employee", IsActive = true }, new User { Id = managerUserId, TenantId = tenantId, Email = $"{managerUserId:N}@test.local", PasswordHash = "test-hash", FirstName = "Notice", LastName = "Manager", IsActive = true }, new User { Id = hrUserId, TenantId = tenantId, Email = $"{hrUserId:N}@test.local", PasswordHash = "test-hash", FirstName = "Notice", LastName = "Hr", IsActive = true });
            seed.Employees.AddRange(new Employee { Id = employeeId, TenantId = tenantId, EmployeeCode = $"N-{employeeId:N}"[..12], FirstName = "Notice", LastName = "Employee", Email = $"{employeeId:N}@test.local", DateOfJoining = new(2025, 1, 1), Status = EmployeeStatus.Active, ReportingManagerId = managerId }, new Employee { Id = managerId, TenantId = tenantId, EmployeeCode = $"M-{managerId:N}"[..12], FirstName = "Notice", LastName = "Manager", Email = $"{managerId:N}@test.local", DateOfJoining = new(2025, 1, 1), Status = EmployeeStatus.Active });
            Link(seed, tenantId, employeeUserId, employeeId); Link(seed, tenantId, managerUserId, managerId);
            seed.EmployeeEmployments.Add(new EmployeeEmployment { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId, FirstHiredDate = new(2025, 1, 1), DateOfJoining = new(2025, 1, 1), NoticePeriod = 30, NoticePeriodUnit = "Days" });
            seed.EmployeeEmploymentHistory.AddRange(new EmployeeEmploymentHistory { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId, EffectiveFrom = new(2020, 1, 1), ManagerId = managerId, EmploymentStatus = EmployeeStatus.Active }, new EmployeeEmploymentHistory { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = managerId, EffectiveFrom = new(2020, 1, 1), EmploymentStatus = EmployeeStatus.Active });
            seed.SeparationReasons.Add(new HRMS.Domain.Entities.Separation.SeparationReason { Id = Guid.NewGuid(), TenantId = tenantId, Code = "NOTICE", Name = "Notice", Category = SeparationReasonCategory.Resignation, EmployeeInitiatedAllowed = true, EffectiveFrom = new(2020, 1, 1), IsActive = true });
            await seed.SaveChangesAsync();
        }
        Guid reasonId;
        await using (var read = createContext(new TestTenantContext(tenantId))) reasonId = await read.SeparationReasons.Select(x => x.Id).SingleAsync();
        Guid separationId;
        await using (var employeeDb = createContext(new TestTenantContext(tenantId, employeeUserId)))
        {
            var service = CreateService(employeeDb, tenantId, employeeUserId); var created = await service.CreateSelfAsync(new(reasonId, new(2026, 9, 23), proposedLwd, "Notice request")); Assert.True(created.Succeeded, created.Message); separationId = created.Value!.Id; Assert.True((await service.SubmitAsync(separationId)).Succeeded);
        }
        await using (var managerDb = createContext(new TestTenantContext(tenantId, managerUserId))) { Assert.True((await CreateService(managerDb, tenantId, managerUserId).ManagerApproveAsync(separationId)).Succeeded); }
        await using (var hrDb = createContext(new TestTenantContext(tenantId, hrUserId))) { var approved = await CreateService(hrDb, tenantId, hrUserId).HrApproveAsync(separationId); Assert.True(approved.Succeeded, approved.Message); }
        return new NoticeFixture(tenantId, employeeId, employeeUserId, managerUserId, hrUserId, separationId);
    }

    private static void Link(HrmsDbContext db, Guid tenantId, Guid userId, Guid employeeId) { var linkId = Guid.NewGuid(); db.AccountEmployeeLinkEvents.Add(new AccountEmployeeLinkEvent { Id = linkId, TenantId = tenantId, SubjectUserId = userId, ActorUserId = userId, Sequence = 1, Operation = "Link", NewLinkId = linkId, AfterEmployeeId = employeeId, OccurredAtUtc = DateTime.UtcNow, Reason = "notice test", CorrelationId = linkId.ToString("N") }); db.AccountEmployeeCurrentLinks.Add(new AccountEmployeeCurrentLink { LinkId = linkId, TenantId = tenantId, UserId = userId, EmployeeId = employeeId }); }
}
