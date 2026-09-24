using HRMS.Application.DTOs.Separation;
using HRMS.Application.Services;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class SeparationClearanceTests
{
    [Fact]
    public async Task Approved_separation_snapshots_template_blocks_until_mandatory_tasks_are_resolved_and_reaches_ready_for_exit()
    {
        using var database = new SqliteInMemoryDatabase();
        var fixture = await NoticeTestData.CreateApprovedAsync(database, new(2026, 10, 2));
        await using var db = database.CreateContext(new TestTenantContext(fixture.TenantId, fixture.HrUserId));
        var service = new ClearanceService(db, new TestTenantContext(fixture.TenantId, fixture.HrUserId), new EmployeeIdentityResolver(db, new TestTenantContext(fixture.TenantId, fixture.HrUserId)), new EmployeeManagerResolver(db, new TestTenantContext(fixture.TenantId, fixture.HrUserId)), TimeProvider.System);
        var template = await service.CreateTemplateAsync(new("EXIT", "Exit clearance", null, new(2020, 1, 1), null, null, null, new[]
        {
            new ClearanceTemplateItemRequest("ASSET", "Laptop return", null, SeparationClearanceTaskCategory.Asset, ClearanceOwnerType.It, null, true, true, true, false, 1, 3),
            new ClearanceTemplateItemRequest("HR", "HR review", null, SeparationClearanceTaskCategory.Hr, ClearanceOwnerType.Hr, null, true, false, true, false, 2, 1)
        }));
        Assert.True(template.Succeeded, template.Message);
        var started = await service.StartAsync(fixture.SeparationId); Assert.True(started.Succeeded, started.Message); Assert.Equal(2, started.Value!.Tasks.Count);
        var asset = started.Value.Tasks.Single(x => x.Code == "ASSET"); var hr = started.Value.Tasks.Single(x => x.Code == "HR");
        Assert.False((await service.CompleteAsync(started.Value.Id)).Succeeded);
        Assert.False((await service.ClearTaskAsync(asset.Id, new("Laptop handed over", null))).Succeeded);
        Assert.True((await service.AddAssetReturnAsync(asset.Id, new("LAP-1", "Laptop", "ThinkPad", "SN-1", new(2026, 10, 1), SeparationAssetReturnStatus.Returned, SeparationAssetCondition.Good, false, null, "Returned"))).Succeeded);
        var clearedAsset = await service.ClearTaskAsync(asset.Id, new("Returned in good condition", null)); Assert.True(clearedAsset.Succeeded, clearedAsset.Message);
        var clearedHr = await service.ClearTaskAsync(hr.Id, new("HR cleared", null)); Assert.True(clearedHr.Succeeded, clearedHr.Message);
        var completed = await service.CompleteAsync(started.Value.Id); Assert.True(completed.Succeeded, completed.Message); Assert.True(completed.Value!.ReadyForExit);
        Assert.Equal(EmployeeSeparationStatus.ReadyForExit, await db.EmployeeSeparations.Where(x => x.Id == fixture.SeparationId).Select(x => x.Status).SingleAsync());
        Assert.Empty(await db.PayrollResults.Where(x => x.EmployeeId == fixture.EmployeeId).ToListAsync());
    }

    [Fact]
    public async Task Completed_clearance_can_be_reopened_without_erasing_history()
    {
        using var database = new SqliteInMemoryDatabase();
        var fixture = await NoticeTestData.CreateApprovedAsync(database, new(2026, 10, 2));
        await using var db = database.CreateContext(new TestTenantContext(fixture.TenantId, fixture.HrUserId));
        var service = new ClearanceService(db, new TestTenantContext(fixture.TenantId, fixture.HrUserId), new EmployeeIdentityResolver(db, new TestTenantContext(fixture.TenantId, fixture.HrUserId)), new EmployeeManagerResolver(db, new TestTenantContext(fixture.TenantId, fixture.HrUserId)), TimeProvider.System);
        Assert.True((await service.CreateTemplateAsync(new("MIN", "Minimal", null, new(2020, 1, 1), null, null, null, new[] { new ClearanceTemplateItemRequest("HR", "HR", null, SeparationClearanceTaskCategory.Hr, ClearanceOwnerType.Hr, null, true, false, false, false, 1, null) }))).Succeeded);
        var started = await service.StartAsync(fixture.SeparationId); Assert.True(started.Succeeded);
        var cleared = await service.ClearTaskAsync(started.Value!.Tasks.Single().Id, new(null, null)); Assert.True(cleared.Succeeded, cleared.Message);
        Assert.True((await service.CompleteAsync(started.Value.Id)).Succeeded);
        var reopened = await service.ReopenAsync(started.Value.Id, "Rework required"); Assert.True(reopened.Succeeded, reopened.Message); Assert.Equal(SeparationClearanceStatus.Reopened, reopened.Value!.Status);
        Assert.Equal(4, await db.SeparationClearanceEvents.CountAsync(x => x.SeparationClearanceId == started.Value.Id));
    }
}
