using HRMS.Application.Abstractions;
using HRMS.Application.DTOs.Separation;
using HRMS.Application.Services;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class SeparationClearanceFailureInjectionTests
{
    [Fact]
    public async Task Task_clear_failure_before_commit_rolls_back()
    {
        using var database = new SqliteInMemoryDatabase();
        var fixture = await NoticeTestData.CreateApprovedAsync(database, new(2026, 10, 2));
        var ids = await PrepareAsync(database, fixture, false);
        var injector = new OneShotClearanceFailureInjector { FailTask = true };
        await using (var failing = database.CreateContext(new TestTenantContext(fixture.TenantId, fixture.HrUserId)))
        {
            var result = await CreateService(failing, fixture, injector).ClearTaskAsync(ids.TaskId, new("cleared", null));
            Assert.False(result.Succeeded);
        }
        await using (var fresh = database.CreateContext(new TestTenantContext(fixture.TenantId, fixture.HrUserId)))
        {
            Assert.Equal(SeparationClearanceTaskStatus.Pending, await fresh.SeparationClearanceTasks.Where(x => x.Id == ids.TaskId).Select(x => x.Status).SingleAsync());
            Assert.Equal(0, await fresh.SeparationClearanceEvents.CountAsync(x => x.TaskId == ids.TaskId && x.EventType == SeparationClearanceEventType.TaskCleared));
        }
        await using var retryDb = database.CreateContext(new TestTenantContext(fixture.TenantId, fixture.HrUserId));
        Assert.True((await CreateService(retryDb, fixture).ClearTaskAsync(ids.TaskId, new("cleared", null))).Succeeded);
    }

    [Fact]
    public async Task Clearance_completion_failure_before_commit_rolls_back()
    {
        using var database = new SqliteInMemoryDatabase();
        var fixture = await NoticeTestData.CreateApprovedAsync(database, new(2026, 10, 2));
        var ids = await PrepareAsync(database, fixture, false);
        await using (var clearDb = database.CreateContext(new TestTenantContext(fixture.TenantId, fixture.HrUserId))) Assert.True((await CreateService(clearDb, fixture).ClearTaskAsync(ids.TaskId, new("cleared", null))).Succeeded);
        var injector = new OneShotClearanceFailureInjector { FailCompletion = true };
        await using (var failing = database.CreateContext(new TestTenantContext(fixture.TenantId, fixture.HrUserId))) Assert.False((await CreateService(failing, fixture, injector).CompleteAsync(ids.ClearanceId)).Succeeded);
        await using (var fresh = database.CreateContext(new TestTenantContext(fixture.TenantId, fixture.HrUserId)))
        {
            Assert.NotEqual(SeparationClearanceStatus.Completed, await fresh.SeparationClearances.Where(x => x.Id == ids.ClearanceId).Select(x => x.Status).SingleAsync());
            Assert.Equal(0, await fresh.SeparationClearanceEvents.CountAsync(x => x.SeparationClearanceId == ids.ClearanceId && x.EventType == SeparationClearanceEventType.ClearanceCompleted));
            Assert.NotEqual(EmployeeSeparationStatus.ReadyForExit, await fresh.EmployeeSeparations.Where(x => x.Id == fixture.SeparationId).Select(x => x.Status).SingleAsync());
        }
        await using var retryDb = database.CreateContext(new TestTenantContext(fixture.TenantId, fixture.HrUserId));
        Assert.True((await CreateService(retryDb, fixture).CompleteAsync(ids.ClearanceId)).Succeeded);
    }

    [Fact]
    public async Task Asset_return_failure_before_commit_rolls_back()
    {
        using var database = new SqliteInMemoryDatabase();
        var fixture = await NoticeTestData.CreateApprovedAsync(database, new(2026, 10, 2));
        var ids = await PrepareAsync(database, fixture, true);
        var request = new SeparationAssetReturnRequest("LAP-1", "Laptop", "ThinkPad", "SN-1", new(2026, 10, 1), SeparationAssetReturnStatus.Returned, SeparationAssetCondition.Good, false, null, "returned");
        var injector = new OneShotClearanceFailureInjector { FailAsset = true };
        await using (var failing = database.CreateContext(new TestTenantContext(fixture.TenantId, fixture.HrUserId))) Assert.False((await CreateService(failing, fixture, injector).AddAssetReturnAsync(ids.TaskId, request)).Succeeded);
        await using (var fresh = database.CreateContext(new TestTenantContext(fixture.TenantId, fixture.HrUserId))) Assert.Equal(0, await fresh.SeparationAssetReturns.CountAsync(x => x.SeparationClearanceTaskId == ids.TaskId));
        await using var retryDb = database.CreateContext(new TestTenantContext(fixture.TenantId, fixture.HrUserId));
        Assert.True((await CreateService(retryDb, fixture).AddAssetReturnAsync(ids.TaskId, request)).Succeeded);
    }

    private static ClearanceService CreateService(HRMS.Infrastructure.Persistence.HrmsDbContext db, NoticeFixture fixture, IClearanceFailureInjector? injector = null)
    {
        var tenant = new TestTenantContext(fixture.TenantId, fixture.HrUserId);
        return new ClearanceService(db, tenant, new EmployeeIdentityResolver(db, tenant), new EmployeeManagerResolver(db, tenant), TimeProvider.System, injector);
    }

    private static async Task<(Guid ClearanceId, Guid TaskId)> PrepareAsync(SqliteInMemoryDatabase database, NoticeFixture fixture, bool asset)
    {
        await using var db = database.CreateContext(new TestTenantContext(fixture.TenantId, fixture.HrUserId));
        var service = CreateService(db, fixture);
        var template = await service.CreateTemplateAsync(new("FAIL" + Guid.NewGuid().ToString("N")[..6], "Failure", null, new(2020, 1, 1), null, null, null, new[] { new ClearanceTemplateItemRequest(asset ? "ASSET" : "HR", asset ? "Asset" : "HR", null, asset ? SeparationClearanceTaskCategory.Asset : SeparationClearanceTaskCategory.Hr, ClearanceOwnerType.Hr, null, true, asset, false, false, 1, null) }));
        Assert.True(template.Succeeded, template.Message);
        var started = await service.StartAsync(fixture.SeparationId); Assert.True(started.Succeeded, started.Message);
        var task = started.Value!.Tasks.Single();
        return (started.Value.Id, task.Id);
    }

    private sealed class OneShotClearanceFailureInjector : IClearanceFailureInjector
    {
        public bool FailTask { get; init; }
        public bool FailCompletion { get; init; }
        public bool FailAsset { get; init; }
        public void BeforeTaskCommit() { if (FailTask) throw new InvalidOperationException("test failure"); }
        public void BeforeCompletionCommit() { if (FailCompletion) throw new InvalidOperationException("test failure"); }
        public void BeforeAssetCommit() { if (FailAsset) throw new InvalidOperationException("test failure"); }
    }
}
