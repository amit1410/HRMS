using HRMS.Application.Abstractions;
using HRMS.Application.Services;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class OvertimePayrollEndToEndTests
{
    [Fact]
    public async Task Attendance_to_ot_to_payroll_preserves_money_and_traceability()
    {
        await using var f = await Phase6BAcceptanceData.FinalizedAsync();
        Assert.True((await f.CalculatePayrollAsync()).Succeeded);
        var rateComponent = await f.Db.SalaryComponents.Select(x => x.Id).FirstAsync();
        var scope = f.Scope;
        var overtime = new OvertimeService(f.Db, scope, TimeProvider.System);
        var day = await f.Db.EmployeeAttendanceDays.SingleAsync(x => x.EmployeeId == f.EmployeeId && x.BusinessDate == new DateOnly(2026, 9, 15));
        day.WorkedMinutes = 540;
        await f.Db.SaveChangesAsync();
        var policy = await overtime.CreatePolicyAsync(new("E2E-OT", "E2E overtime", new(2026, 1, 1), null, 0, 0, OvertimeRoundingMode.None, null, null, false, true, true, true, true, 1.5m, 2m, 2m, "All", rateComponent, 2400));
        Assert.True(policy.Succeeded, policy.Message);
        var request = await overtime.CreateRequestAsync(new(f.EmployeeId, new(2026, 9, 15), 60, OvertimeCategory.NormalDay, "e2e"));
        Assert.True(request.Succeeded, request.Message);
        Assert.Equal(60, request.Value!.ActualEligibleMinutes);
        Assert.True((await overtime.SubmitAsync(request.Value.Id)).Succeeded);
        var approved = await overtime.ApproveAsync(request.Value.Id, new());
        Assert.True(approved.Succeeded, approved.Message);
        Assert.Equal(60, approved.Value!.ApprovedMinutes);
        var finalized = await overtime.FinalizeAsync(f.PeriodId);
        Assert.True(finalized.Succeeded, finalized.Message);
        var snapshot = finalized.Value!.Single();
        Assert.Equal(60, snapshot.TotalApprovedMinutes);
        Assert.Equal(1, snapshot.Version);
        var resolved = await overtime.ResolveAsync(f.EmployeeId, new(2026, 9, 1), new(2026, 9, 30));
        Assert.True(resolved.Succeeded, resolved.Message);
        Assert.Equal(snapshot.SnapshotId, resolved.Value!.SnapshotId);
        Assert.Equal(snapshot.AttendanceSnapshotId, resolved.Value.AttendanceSnapshotId);
        var payroll = await new PayrollCalculationEngine(f.Db, scope, TimeProvider.System, attendance: new AttendancePayrollSnapshotResolver(f.Db, scope), overtime: overtime).CalculateAsync(f.PayrollRunId, true);
        Assert.True(payroll.Succeeded, payroll.Message);
        var result = await f.Db.PayrollResults.Include(x => x.Components).SingleAsync(x => x.IsCurrent);
        var earning = result.Components.Single(x => x.CalculationSource == "PayrollOvertimeSnapshot");
        Assert.Equal(375m, earning.CalculatedAmount);
        Assert.Equal(snapshot.SnapshotId, result.PayrollOvertimeSnapshotId);
        Assert.Equal(snapshot.Version, result.OvertimeVersion);
        Assert.Equal(snapshot.AttendanceSnapshotId, result.AttendanceSnapshotId);
        Assert.Equal(snapshot.AttendanceVersion, result.AttendanceVersion);
    }
}
