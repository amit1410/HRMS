using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.Services;
using HRMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HRMS.Tests;

public sealed class OvertimeFailureInjectionTests
{
    [Fact]
    public async Task Approval_persistence_failure_retry_safe()
    {
        var injector = new ThrowOnSave(4);
        await using var f = await PreparedRequestAsync(injector);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.ApproveAsync(f.RequestId, new()));
        var verify = f.CreateIndependentService();
        Assert.Equal(0, await verify.Db.OvertimeRequests.AsNoTracking().CountAsync(x => x.Status == HRMS.Domain.Enums.OvertimeRequestStatus.Approved));
        Assert.True((await verify.Service.ApproveAsync(f.RequestId, new())).Succeeded);
        Assert.Equal(1, await verify.Db.OvertimeRequests.AsNoTracking().CountAsync(x => x.Status == HRMS.Domain.Enums.OvertimeRequestStatus.Approved)); await verify.Db.DisposeAsync();
    }

    [Fact]
    public async Task Monthly_finalization_failure_does_not_false_finalize()
    {
        var injector = new ThrowOnSave(5);
        await using var f = await FinalizableAsync(injector);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.FinalizeAsync(f.PeriodId));
        var verify = f.CreateIndependentService();
        Assert.Empty(await verify.Db.PayrollOvertimeSnapshots.AsNoTracking().ToListAsync());
        Assert.True((await verify.Service.FinalizeAsync(f.PeriodId)).Succeeded); await verify.Db.DisposeAsync();
    }

    [Fact]
    public async Task Payroll_snapshot_persistence_failure_retry_safe()
    {
        var injector = new ThrowOnSave(5);
        await using var f = await FinalizableAsync(injector);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.FinalizeAsync(f.PeriodId));
        var verify = f.CreateIndependentService();
        Assert.Equal(0, await verify.Db.PayrollOvertimeSnapshots.AsNoTracking().CountAsync(x => x.IsCurrent));
        Assert.True((await verify.Service.FinalizeAsync(f.PeriodId)).Succeeded);
        Assert.Equal(1, await verify.Db.PayrollOvertimeSnapshots.AsNoTracking().CountAsync(x => x.IsCurrent)); await verify.Db.DisposeAsync();
    }

    [Fact]
    public async Task Reopen_failure_preserves_previous_version()
    {
        var injector = new ThrowOnSave(6);
        await using var f = await FinalizableAsync(injector);
        Assert.True((await f.Service.FinalizeAsync(f.PeriodId)).Succeeded);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.ReopenAsync(f.PeriodId, "injected failure"));
        var verify = f.CreateIndependentService();
        Assert.Equal(1, await verify.Db.PayrollOvertimeSnapshots.AsNoTracking().CountAsync(x => x.IsCurrent && x.Version == 1)); await verify.Db.DisposeAsync();
    }

    [Fact]
    public async Task Payroll_ot_resolution_failure_does_not_partially_mutate_payroll()
    {
        await using var f = await Phase6BAcceptanceData.FinalizedAsync();
        Assert.True((await f.CalculatePayrollAsync()).Succeeded);
        var before = await f.Db.PayrollResults.AsNoTracking().CountAsync(x => x.IsCurrent);
        var result = await new PayrollCalculationEngine(f.Db, f.Scope, TimeProvider.System, attendance: new AttendancePayrollSnapshotResolver(f.Db, f.Scope), overtime: new FailureResolver()).CalculateAsync(f.PayrollRunId, true);
        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(before, await f.Db.PayrollResults.AsNoTracking().CountAsync(x => x.IsCurrent));
        Assert.Empty(await f.Db.PayrollResultComponents.AsNoTracking().Where(x => x.CalculationSource == "PayrollOvertimeSnapshot" && x.PayrollResult!.IsCurrent).ToListAsync());
    }

    private static async Task<OvertimeAcceptanceTests.OvertimeFixture> PreparedRequestAsync(IInterceptor injector)
    {
        var f = await OvertimeAcceptanceTests.OvertimeFixture.CreateAsync(540, true, injector);
        Assert.True((await f.Service.CreatePolicyAsync(f.PolicyRequest())).Succeeded);
        var request = await f.Service.CreateRequestAsync(new(f.EmployeeId, f.WorkDate, 60, HRMS.Domain.Enums.OvertimeCategory.NormalDay, "failure"));
        Assert.True(request.Succeeded, request.Message); f.RequestId = request.Value!.Id;
        Assert.True((await f.Service.SubmitAsync(f.RequestId)).Succeeded);
        return f;
    }

    private static async Task<OvertimeAcceptanceTests.OvertimeFixture> FinalizableAsync(IInterceptor injector)
    {
        var f = await PreparedRequestAsync(injector);
        Assert.True((await f.Service.ApproveAsync(f.RequestId, new())).Succeeded);
        return f;
    }

    private sealed class ThrowOnSave(int throwAt) : SaveChangesInterceptor
    {
        private int saves;
        public int ThrowAt { get; set; } = throwAt;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref saves) == ThrowAt) throw new InvalidOperationException("injected overtime persistence failure");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class FailureResolver : IPayrollOvertimeSnapshotResolver
    {
        public Task<Result<PayrollOvertimeSnapshotContract?>> ResolveAsync(Guid employeeId, DateOnly periodStart, DateOnly periodEnd, CancellationToken ct = default) => Task.FromResult(Result<PayrollOvertimeSnapshotContract?>.Conflict("injected OT resolution failure"));
    }
}
