using HRMS.Application.DTOs.Separation;
using HRMS.Application.Services;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class SeparationClearanceConcurrencyTests
{
    [Fact] public async Task Clear_vs_block_has_one_authoritative_task_state() => await AssertSingleWinnerAsync(SeparationClearanceTaskStatus.Cleared, SeparationClearanceTaskStatus.Blocked);
    [Fact] public async Task Clear_vs_waive_has_one_authoritative_task_state() => await AssertSingleWinnerAsync(SeparationClearanceTaskStatus.Cleared, SeparationClearanceTaskStatus.Waived);

    [Fact]
    public async Task Complete_vs_task_update_never_completes_with_unresolved_mandatory_task()
    {
        using var database = new SqliteInMemoryDatabase(); var f = await NoticeTestData.CreateApprovedAsync(database, new(2026, 10, 2)); var ids = await PrepareAsync(database, f, false);
        var barrier = new Barrier(2); await using var a = database.CreateContext(new TestTenantContext(f.TenantId, f.HrUserId)); await using var b = database.CreateContext(new TestTenantContext(f.TenantId, f.HrUserId));
        var results = await Task.WhenAll(Service(a, f, new BarrierInjector(barrier)).CompleteAsync(ids.ClearanceId), Service(b, f, new BarrierInjector(barrier)).BlockTaskAsync(ids.TaskId, new(null, "blocked")));
        await using var verify = database.CreateContext(new TestTenantContext(f.TenantId, f.HrUserId)); var row = await verify.SeparationClearances.Include(x => x.Tasks).SingleAsync(x => x.Id == ids.ClearanceId);
        Assert.False(row.Status == SeparationClearanceStatus.Completed && row.Tasks.Any(x => x.IsMandatory && !IsResolved(x.Status))); Assert.InRange(results.Count(x => x.Succeeded), 0, 1);
    }

    [Fact]
    public async Task Reopen_vs_complete_leaves_a_valid_clearance_state()
    {
        using var database = new SqliteInMemoryDatabase(); var f = await NoticeTestData.CreateApprovedAsync(database, new(2026, 10, 2)); var ids = await PrepareAsync(database, f, false); await using (var setup = database.CreateContext(new TestTenantContext(f.TenantId, f.HrUserId))) { var s = Service(setup, f); Assert.True((await s.ClearTaskAsync(ids.TaskId, new("ok", null))).Succeeded); Assert.True((await s.CompleteAsync(ids.ClearanceId)).Succeeded); }
        await using var a = database.CreateContext(new TestTenantContext(f.TenantId, f.HrUserId)); await using var b = database.CreateContext(new TestTenantContext(f.TenantId, f.HrUserId)); var results = await Task.WhenAll(Service(a, f).ReopenAsync(ids.ClearanceId, "rework"), Service(b, f).CompleteAsync(ids.ClearanceId));
        await using var verify = database.CreateContext(new TestTenantContext(f.TenantId, f.HrUserId)); var status = await verify.SeparationClearances.Where(x => x.Id == ids.ClearanceId).Select(x => x.Status).SingleAsync(); Assert.Contains(status, new[] { SeparationClearanceStatus.Completed, SeparationClearanceStatus.Reopened }); Assert.InRange(results.Count(x => x.Succeeded), 1, 2);
    }

    [Fact]
    public async Task Asset_return_vs_lost_has_one_final_asset_state()
    {
        using var database = new SqliteInMemoryDatabase(); var f = await NoticeTestData.CreateApprovedAsync(database, new(2026, 10, 2)); var ids = await PrepareAsync(database, f, true); var request = new SeparationAssetReturnRequest("LAP", "Laptop", "Device", "SN", new(2026, 10, 2), SeparationAssetReturnStatus.Returned, SeparationAssetCondition.Good, false, null, "returned"); await using (var setup = database.CreateContext(new TestTenantContext(f.TenantId, f.HrUserId))) Assert.True((await Service(setup, f).AddAssetReturnAsync(ids.TaskId, request)).Succeeded); await using var assetDb = database.CreateContext(new TestTenantContext(f.TenantId, f.HrUserId)); var assetId = await assetDb.SeparationAssetReturns.Where(x => x.SeparationClearanceTaskId == ids.TaskId).Select(x => x.Id).SingleAsync();
        var lost = request with { ReturnStatus = SeparationAssetReturnStatus.Lost, Condition = SeparationAssetCondition.Lost, RecoveryRequired = true, Comment = "lost" }; var barrier = new Barrier(2); await using var a = database.CreateContext(new TestTenantContext(f.TenantId, f.HrUserId)); await using var b = database.CreateContext(new TestTenantContext(f.TenantId, f.HrUserId)); var results = await Task.WhenAll(Service(a, f, new BarrierInjector(barrier)).UpdateAssetReturnAsync(ids.TaskId, assetId, request), Service(b, f, new BarrierInjector(barrier)).UpdateAssetReturnAsync(ids.TaskId, assetId, lost)); await using var verify = database.CreateContext(new TestTenantContext(f.TenantId, f.HrUserId)); var final = await verify.SeparationAssetReturns.SingleAsync(x => x.Id == assetId); Assert.Contains(final.ReturnStatus, new[] { SeparationAssetReturnStatus.Returned, SeparationAssetReturnStatus.Lost }); Assert.InRange(results.Count(x => x.Succeeded), 0, 1);
    }

    [Fact]
    public async Task Lwd_revision_vs_completion_does_not_create_impossible_ready_state()
    {
        using var database = new SqliteInMemoryDatabase(); var f = await NoticeTestData.CreateApprovedAsync(database, new(2026, 10, 2)); var ids = await PrepareAsync(database, f, false); await using (var setup = database.CreateContext(new TestTenantContext(f.TenantId, f.HrUserId))) { var s = Service(setup, f); Assert.True((await s.ClearTaskAsync(ids.TaskId, new("ok", null))).Succeeded); }
        await using var a = database.CreateContext(new TestTenantContext(f.TenantId, f.HrUserId)); await using var b = database.CreateContext(new TestTenantContext(f.TenantId, f.HrUserId)); var completion = Service(a, f).CompleteAsync(ids.ClearanceId); var revision = NoticeTestData.CreateService(b, f.TenantId, f.HrUserId).ReviseApprovedLwdAsync(f.SeparationId, new(new(2026, 10, 5), "schedule change")); await Task.WhenAll(completion, revision); await using var verify = database.CreateContext(new TestTenantContext(f.TenantId, f.HrUserId)); var separation = await verify.EmployeeSeparations.SingleAsync(x => x.Id == f.SeparationId); var clearance = await verify.SeparationClearances.SingleAsync(x => x.Id == ids.ClearanceId); Assert.True(separation.Status is EmployeeSeparationStatus.ReadyForExit or EmployeeSeparationStatus.Approved or EmployeeSeparationStatus.NoticePeriod); Assert.NotEqual(SeparationClearanceStatus.Cancelled, clearance.Status); Assert.InRange(new[] { completion.Result.Succeeded, revision.Result.Succeeded }.Count(x => x), 1, 2);
    }

    private async Task AssertSingleWinnerAsync(SeparationClearanceTaskStatus first, SeparationClearanceTaskStatus second)
    {
        using var database = new SqliteInMemoryDatabase(); var f = await NoticeTestData.CreateApprovedAsync(database, new(2026, 10, 2)); var ids = await PrepareAsync(database, f, false); var barrier = new Barrier(2); await using var a = database.CreateContext(new TestTenantContext(f.TenantId, f.HrUserId)); await using var b = database.CreateContext(new TestTenantContext(f.TenantId, f.HrUserId)); var left = Service(a, f, new BarrierInjector(barrier)); var right = Service(b, f, new BarrierInjector(barrier));
        var leftTask = first == SeparationClearanceTaskStatus.Cleared ? left.ClearTaskAsync(ids.TaskId, new("clear", null)) : left.WaiveTaskAsync(ids.TaskId, new(null, "waive"));
        var rightTask = second == SeparationClearanceTaskStatus.Blocked ? right.BlockTaskAsync(ids.TaskId, new(null, "block")) : right.WaiveTaskAsync(ids.TaskId, new(null, "waive"));
        var results = await Task.WhenAll(leftTask, rightTask); await using var verify = database.CreateContext(new TestTenantContext(f.TenantId, f.HrUserId)); var task = await verify.SeparationClearanceTasks.SingleAsync(x => x.Id == ids.TaskId); Assert.Contains(task.Status, new[] { SeparationClearanceTaskStatus.Cleared, SeparationClearanceTaskStatus.Blocked, SeparationClearanceTaskStatus.Waived }); Assert.InRange(results.Count(x => x.Succeeded), 0, 1);
    }

    private static bool IsResolved(SeparationClearanceTaskStatus s) => s is SeparationClearanceTaskStatus.Cleared or SeparationClearanceTaskStatus.Waived or SeparationClearanceTaskStatus.NotApplicable;
    private static ClearanceService Service(HRMS.Infrastructure.Persistence.HrmsDbContext db, NoticeFixture f, HRMS.Application.Abstractions.IClearanceFailureInjector? injector = null) { var t = new TestTenantContext(f.TenantId, f.HrUserId); return new(db, t, new EmployeeIdentityResolver(db, t), new EmployeeManagerResolver(db, t), TimeProvider.System, injector); }
    private static async Task<(Guid ClearanceId, Guid TaskId)> PrepareAsync(SqliteInMemoryDatabase database, NoticeFixture f, bool asset) { await using var db = database.CreateContext(new TestTenantContext(f.TenantId, f.HrUserId)); var s = Service(db, f); var template = await s.CreateTemplateAsync(new("RACE" + Guid.NewGuid().ToString("N")[..5], "Race", null, new(2020, 1, 1), null, null, null, new[] { new ClearanceTemplateItemRequest(asset ? "ASSET" : "HR", "Task", null, asset ? SeparationClearanceTaskCategory.Asset : SeparationClearanceTaskCategory.Hr, ClearanceOwnerType.Hr, null, true, asset, false, false, 1, null) })); Assert.True(template.Succeeded); var started = await s.StartAsync(f.SeparationId); Assert.True(started.Succeeded); var task = started.Value!.Tasks.Single(); return (started.Value.Id, task.Id); }
}

internal sealed class BarrierInjector(Barrier barrier) : HRMS.Application.Abstractions.IClearanceFailureInjector
{
    public void BeforeTaskCommit() => barrier.SignalAndWait(TimeSpan.FromSeconds(10));
    public void BeforeCompletionCommit() => barrier.SignalAndWait(TimeSpan.FromSeconds(10));
    public void BeforeAssetCommit() => barrier.SignalAndWait(TimeSpan.FromSeconds(10));
}
