using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HRMS.Tests;

public sealed class SeparationNoticeFailureInjectionTests
{
    [Fact]
    public async Task Lwd_revision_employment_sync_failure_rolls_back_all()
    {
        using var database = new SqliteInMemoryDatabase();
        var fixture = await NoticeTestData.CreateApprovedAsync(database, new(2026, 10, 2));
        await using (var failing = database.CreateContext(new TestTenantContext(fixture.TenantId, fixture.HrUserId), new ThrowOnceOnNoticeSaveInterceptor()))
            Assert.False((await NoticeTestData.CreateService(failing, fixture.TenantId, fixture.HrUserId).ReviseApprovedLwdAsync(fixture.SeparationId, new(new(2026, 10, 10), "Injected sync failure"))).Succeeded);
        await using var fresh = database.CreateContext(new TestTenantContext(fixture.TenantId, fixture.HrUserId));
        Assert.Equal(new(2026, 10, 2), (await fresh.EmployeeSeparations.SingleAsync(x => x.Id == fixture.SeparationId)).ApprovedLastWorkingDate);
        Assert.Equal(new(2026, 10, 2), (await fresh.EmployeeEmployments.SingleAsync(x => x.EmployeeId == fixture.EmployeeId)).NoticeEndDate);
    }

    [Fact]
    public async Task Waiver_failure_before_commit_rolls_back()
    {
        using var database = new SqliteInMemoryDatabase();
        var fixture = await NoticeTestData.CreateApprovedAsync(database, new(2026, 10, 2));
        await using (var failing = database.CreateContext(new TestTenantContext(fixture.TenantId, fixture.HrUserId), new ThrowOnceOnNoticeSaveInterceptor()))
            Assert.False((await NoticeTestData.CreateService(failing, fixture.TenantId, fixture.HrUserId).ApplyNoticeWaiverAsync(fixture.SeparationId, new(5, "Injected waiver failure"))).Succeeded);
        await using var fresh = database.CreateContext(new TestTenantContext(fixture.TenantId, fixture.HrUserId));
        var row = await fresh.EmployeeSeparations.SingleAsync(x => x.Id == fixture.SeparationId);
        Assert.Equal(0, row.WaivedNoticeDays); Assert.Equal(0, await fresh.EmployeeSeparationEvents.CountAsync(x => x.EmployeeSeparationId == fixture.SeparationId && x.EventType == EmployeeSeparationEventType.NoticeWaiverApplied));
    }

    [Fact]
    public async Task Durable_success_replay_of_same_lwd_is_safe()
    {
        using var database = new SqliteInMemoryDatabase();
        var fixture = await NoticeTestData.CreateApprovedAsync(database, new(2026, 10, 2));
        await using var db = database.CreateContext(new TestTenantContext(fixture.TenantId, fixture.HrUserId));
        var service = NoticeTestData.CreateService(db, fixture.TenantId, fixture.HrUserId);
        Assert.True((await service.ReviseApprovedLwdAsync(fixture.SeparationId, new(new(2026, 10, 10), "First durable revision"))).Succeeded);
        Assert.True((await service.ReviseApprovedLwdAsync(fixture.SeparationId, new(new(2026, 10, 10), "Replay"))).Succeeded);
        Assert.Equal(1, await db.EmployeeSeparationEvents.CountAsync(x => x.EmployeeSeparationId == fixture.SeparationId && (x.EventType == EmployeeSeparationEventType.ApprovedLwdRevised || x.EventType == EmployeeSeparationEventType.ApprovedLwdExtended || x.EventType == EmployeeSeparationEventType.ApprovedLwdReduced)));
    }
}
