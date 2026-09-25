using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class OvertimeConcurrencyTests
{
    [Fact]
    public async Task Approve_vs_approve()
    {
        await using var f = await ApprovedRequestAsync();
        var a = f.CreateIndependentService(); var b = f.CreateIndependentService();
        try
        {
            var results = await Task.WhenAll(a.Service.ApproveAsync(f.RequestId, new()), b.Service.ApproveAsync(f.RequestId, new()));
            Assert.Single(results, x => x.Succeeded);
            Assert.Equal(1, await f.Db.OvertimeRequests.CountAsync(x => x.Status == OvertimeRequestStatus.Approved));
            Assert.Equal(0, await f.Db.OvertimeRequests.CountAsync(x => x.ApprovedMinutes > x.ActualEligibleMinutes));
        }
        finally { await a.Db.DisposeAsync(); await b.Db.DisposeAsync(); }
    }

    [Fact]
    public async Task Approve_vs_attendance_regularization()
    {
        await using var f = await ApprovedRequestAsync();
        var mutation = f.CreateIndependentService();
        var approval = f.CreateIndependentService();
        try
        {
            var dayTask = Task.Run(async () => { var day = await mutation.Db.EmployeeAttendanceDays.SingleAsync(x => x.EmployeeId == f.EmployeeId); day.WorkedMinutes = 480; await mutation.Db.SaveChangesAsync(); });
            var approvalTask = approval.Service.ApproveAsync(f.RequestId, new());
            await Task.WhenAll(dayTask, approvalTask);
            Assert.True(await f.Db.OvertimeRequests.AnyAsync(x => x.Id == f.RequestId && x.Status == OvertimeRequestStatus.Approved) || await f.Db.OvertimeRequests.AnyAsync(x => x.Id == f.RequestId && x.Status == OvertimeRequestStatus.Submitted));
        }
        finally { await mutation.Db.DisposeAsync(); await approval.Db.DisposeAsync(); }
    }

    [Fact]
    public async Task Approve_vs_leave_change()
    {
        await using var f = await ApprovedRequestAsync();
        var mutation = f.CreateIndependentService();
        var approval = f.CreateIndependentService();
        try
        {
            var leaveTask = Task.Run(async () => { var day = await mutation.Db.EmployeeAttendanceDays.SingleAsync(x => x.EmployeeId == f.EmployeeId); day.Status = EmployeeAttendanceDayStatus.OnLeave; day.WorkedMinutes = null; await mutation.Db.SaveChangesAsync(); });
            var approvalTask = approval.Service.ApproveAsync(f.RequestId, new());
            await Task.WhenAll(leaveTask, approvalTask);
            Assert.True(await f.Db.OvertimeRequests.AnyAsync(x => x.Id == f.RequestId && (x.Status == OvertimeRequestStatus.Approved || x.Status == OvertimeRequestStatus.Submitted)));
        }
        finally { await mutation.Db.DisposeAsync(); await approval.Db.DisposeAsync(); }
    }

    [Fact]
    public async Task Finalize_vs_finalize()
    {
        await using var f = await FinalizableAsync();
        var a = f.CreateIndependentService(); var b = f.CreateIndependentService();
        try { await ObserveRaceAsync(a.Service.FinalizeAsync(f.PeriodId), b.Service.FinalizeAsync(f.PeriodId)); Assert.Equal(1, await f.Db.PayrollOvertimeSnapshots.CountAsync(x => x.IsCurrent)); Assert.Equal(1, await f.Db.PayrollOvertimeSnapshots.Select(x => x.Version).Distinct().CountAsync(x => x == 1)); }
        finally { await a.Db.DisposeAsync(); await b.Db.DisposeAsync(); }
    }

    [Fact]
    public async Task Finalize_vs_ot_approval()
    {
        await using var f = await FinalizableAsync();
        var a = f.CreateIndependentService(); var b = f.CreateIndependentService();
        try { await ObserveRaceAsync(a.Service.FinalizeAsync(f.PeriodId), b.Service.ApproveAsync(f.RequestId, new())); Assert.True(await f.Db.PayrollOvertimeSnapshots.CountAsync(x => x.IsCurrent) <= 1); }
        finally { await a.Db.DisposeAsync(); await b.Db.DisposeAsync(); }
    }

    [Fact]
    public async Task Reopen_vs_payroll_calculate()
    {
        await using var f = await FinalizableAsync();
        var a = f.CreateIndependentService(); var b = f.CreateIndependentService();
        try { await ObserveRaceAsync(a.Service.ReopenAsync(f.PeriodId, "race correction"), b.Service.ResolveAsync(f.EmployeeId, new(2026, 9, 1), new(2026, 9, 30))); Assert.True(await f.Db.PayrollOvertimeSnapshots.CountAsync(x => x.IsCurrent) <= 1); }
        finally { await a.Db.DisposeAsync(); await b.Db.DisposeAsync(); }
    }

    [Fact]
    public async Task Refinalize_vs_payroll_calculate()
    {
        await using var f = await FinalizableAsync();
        Assert.True((await f.Service.ReopenAsync(f.PeriodId, "refinalization correction")).Succeeded);
        var a = f.CreateIndependentService(); var b = f.CreateIndependentService();
        try { await ObserveRaceAsync(a.Service.FinalizeAsync(f.PeriodId), b.Service.ResolveAsync(f.EmployeeId, new(2026, 9, 1), new(2026, 9, 30))); Assert.True(await f.Db.PayrollOvertimeSnapshots.CountAsync(x => x.IsCurrent) <= 1); }
        finally { await a.Db.DisposeAsync(); await b.Db.DisposeAsync(); }
    }

    [Fact]
    public async Task Duplicate_current_ot_snapshot_race()
    {
        await using var f = await FinalizableAsync();
        var a = f.CreateIndependentService(); var b = f.CreateIndependentService();
        try { await ObserveRaceAsync(a.Service.FinalizeAsync(f.PeriodId), b.Service.FinalizeAsync(f.PeriodId)); Assert.True(await f.Db.PayrollOvertimeSnapshots.CountAsync(x => x.IsCurrent) <= 1); }
        finally { await a.Db.DisposeAsync(); await b.Db.DisposeAsync(); }
    }

    private static async Task<OvertimeAcceptanceTests.OvertimeFixture> ApprovedRequestAsync()
    {
        var f = await OvertimeAcceptanceTests.OvertimeFixture.CreateAsync();
        Assert.True((await f.Service.CreatePolicyAsync(f.PolicyRequest())).Succeeded);
        var request = await f.Service.CreateRequestAsync(new(f.EmployeeId, f.WorkDate, 60, OvertimeCategory.NormalDay, "race"));
        Assert.True(request.Succeeded, request.Message); Assert.True((await f.Service.SubmitAsync(request.Value!.Id)).Succeeded);
        f.RequestId = request.Value.Id;
        return f;
    }

    private static async Task<OvertimeAcceptanceTests.OvertimeFixture> FinalizableAsync()
    {
        var f = await ApprovedRequestAsync(); Assert.True((await f.Service.ApproveAsync(f.RequestId, new())).Succeeded); Assert.True((await f.Service.FinalizeAsync(f.PeriodId)).Succeeded); return f;
    }

    private static async Task ObserveRaceAsync(params Task[] tasks)
    {
        try { await Task.WhenAll(tasks); } catch (DbUpdateException) { }
    }
}
