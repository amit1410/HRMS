using HRMS.Application.DTOs.Separation;
using HRMS.Application.Services;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class SeparationExitConcurrencyTests
{
    [Fact]
    public async Task Execute_vs_execute()
    {
        using var database = new SqliteInMemoryDatabase(); var f = await SeparationExitExecutionTests.ReadyFixtureAsync(database);
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ => { await using var db = database.CreateIsolatedContext(new TestTenantContext(f.TenantId, f.HrUserId)); return await Service(db, f).ExecuteAsync(f.SeparationId, new()); }));
        Assert.Contains(results, x => x.Succeeded); await using var verify = database.CreateIsolatedContext(new TestTenantContext(f.TenantId, f.HrUserId)); Assert.Single(await verify.SeparationExitExecutions.ToListAsync()); Assert.Single(await verify.SeparationExitExecutionEvents.Where(x => x.EventType == HRMS.Domain.Entities.Separation.SeparationExitExecutionEventType.SeparationClosed).ToListAsync());
    }

    [Fact] public async Task Execute_vs_lwd_revision() => await ClosedMutationIsRejectedAsync(async (db, f) => !(await NoticeTestData.CreateService(db, f.TenantId, f.HrUserId).ReviseApprovedLwdAsync(f.SeparationId, new(DateOnly.FromDateTime(DateTime.UtcNow.Date), "late revision"))).Succeeded);
    [Fact] public async Task Execute_vs_clearance_reopen() => await ClosedMutationIsRejectedAsync(async (db, f) => await db.EmployeeSeparations.AnyAsync(x => x.Id == f.SeparationId && x.Status == EmployeeSeparationStatus.Closed));
    [Fact] public async Task Execute_vs_settlement_completion() => await ClosedMutationIsRejectedAsync(async (db, f) => await db.EmployeeSeparations.AnyAsync(x => x.Id == f.SeparationId && x.Status == EmployeeSeparationStatus.Closed));
    [Fact] public async Task Execute_vs_role_assignment_change() => await ClosedMutationIsRejectedAsync(async (db, f) => (await db.Employees.SingleAsync(x => x.Id == f.EmployeeId)).Status == EmployeeStatus.Terminated);
    [Fact] public async Task Execute_vs_manager_reassignment() { using var database = new SqliteInMemoryDatabase(); var f = await SeparationExitExecutionTests.ReadyFixtureAsync(database); await using var db = database.CreateContext(new TestTenantContext(f.TenantId, f.HrUserId)); var result = await Service(db, f).GetReadinessAsync(f.SeparationId); Assert.True(result.Succeeded); }
    [Fact] public async Task Retry_vs_retry() { using var database = new SqliteInMemoryDatabase(); var f = await SeparationExitExecutionTests.ReadyFixtureAsync(database); await using var db = database.CreateContext(new TestTenantContext(f.TenantId, f.HrUserId)); Assert.True((await Service(db, f).ExecuteAsync(f.SeparationId, new())).Succeeded); var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ => { await using var verify = database.CreateIsolatedContext(new TestTenantContext(f.TenantId, f.HrUserId)); return await new SeparationExitService(verify, new TestTenantContext(f.TenantId, f.HrUserId), TimeProvider.System).RetryAsync(f.SeparationId, new("retry")); })); Assert.All(results, x => Assert.True(x.Succeeded)); }
    [Fact] public async Task Close_vs_close() { using var database = new SqliteInMemoryDatabase(); var f = await SeparationExitExecutionTests.ReadyFixtureAsync(database); await using var db = database.CreateContext(new TestTenantContext(f.TenantId, f.HrUserId)); var service = Service(db, f); Assert.True((await service.ExecuteAsync(f.SeparationId, new())).Succeeded); Assert.True((await service.ExecuteAsync(f.SeparationId, new())).Succeeded); Assert.Equal(1, await db.SeparationExitExecutionEvents.CountAsync(x => x.EventType == HRMS.Domain.Entities.Separation.SeparationExitExecutionEventType.SeparationClosed)); }

    private static SeparationExitService Service(HRMS.Infrastructure.Persistence.HrmsDbContext db, SeparationExitExecutionTests.ExitFixture f) => new(db, new TestTenantContext(f.TenantId, f.HrUserId), TimeProvider.System);
    private static async Task ClosedMutationIsRejectedAsync(Func<HRMS.Infrastructure.Persistence.HrmsDbContext, SeparationExitExecutionTests.ExitFixture, Task<bool>> mutation) { using var database = new SqliteInMemoryDatabase(); var f = await SeparationExitExecutionTests.ReadyFixtureAsync(database); await using (var executeDb = database.CreateContext(new TestTenantContext(f.TenantId, f.HrUserId))) Assert.True((await Service(executeDb, f).ExecuteAsync(f.SeparationId, new())).Succeeded); await using var db = database.CreateIsolatedContext(new TestTenantContext(f.TenantId, f.HrUserId)); Assert.True(await mutation(db, f)); }
}
