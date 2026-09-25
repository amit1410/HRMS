using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class OvertimePayrollIntegrationTests
{
    [Fact]
    public async Task Payroll_consumes_finalized_ot_snapshot()
    {
        await using var f = await Phase6BAcceptanceData.FinalizedAsync();
        var result = await CalculateAsync(f, Contract(f, normal: 60, version: 1));
        Assert.True(result.Succeeded, result.Message);
        Assert.Contains(await CurrentOtComponents(f), x => x.CalculatedAmount == 375m);
    }

    [Fact]
    public async Task Payroll_rejects_unfinalized_ot()
    {
        await using var f = await Phase6BAcceptanceData.FinalizedAsync();
        var result = await CalculateAsync(f, new FailureResolver("OvertimeNotFinalized: the OT period is not finalized."));
        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(1, result.Value!.FailedCount);
        Assert.Empty(await CurrentOtComponents(f));
    }

    [Fact]
    public async Task Payroll_uses_correct_ot_version()
    {
        await using var f = await Phase6BAcceptanceData.FinalizedAsync();
        var contract = Contract(f, normal: 60, version: 7);
        Assert.True((await CalculateAsync(f, contract)).Succeeded);
        var payroll = await f.Db.PayrollResults.SingleAsync(x => x.IsCurrent);
        Assert.Equal(contract.SnapshotId, payroll.PayrollOvertimeSnapshotId);
        Assert.Equal(7, payroll.OvertimeVersion);
    }

    [Fact]
    public async Task OT_amount_calculated_in_payroll_not_attendance()
    {
        await using var f = await Phase6BAcceptanceData.FinalizedAsync();
        var contract = Contract(f, normal: 60, version: 1);
        Assert.True((await CalculateAsync(f, contract)).Succeeded);
        Assert.DoesNotContain(typeof(PayrollOvertimeSnapshot).GetProperties(), x => x.Name.Contains("Amount", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(await CurrentOtComponents(f), x => x.CalculationSource == "PayrollOvertimeSnapshot");
    }

    [Fact]
    public async Task Normal_day_multiplier_applied_once()
    {
        await using var f = await Phase6BAcceptanceData.FinalizedAsync();
        Assert.True((await CalculateAsync(f, Contract(f, normal: 60, normalMultiplier: 1.5m))).Succeeded);
        Assert.Equal(375m, (await CurrentOtComponents(f)).Single().CalculatedAmount);
    }

    [Fact]
    public async Task WeekOff_multiplier_applied_once()
    {
        await using var f = await Phase6BAcceptanceData.FinalizedAsync();
        Assert.True((await CalculateAsync(f, Contract(f, weekOff: 60, weekMultiplier: 2m))).Succeeded);
        Assert.Equal(500m, (await CurrentOtComponents(f)).Single().CalculatedAmount);
    }

    [Fact]
    public async Task Holiday_multiplier_applied_once()
    {
        await using var f = await Phase6BAcceptanceData.FinalizedAsync();
        Assert.True((await CalculateAsync(f, Contract(f, holiday: 60, holidayMultiplier: 2m))).Succeeded);
        Assert.Equal(500m, (await CurrentOtComponents(f)).Single().CalculatedAmount);
    }

    [Fact]
    public async Task Corrected_ot_does_not_silently_mutate_existing_payroll()
    {
        await using var f = await Phase6BAcceptanceData.FinalizedAsync();
        var v1 = Contract(f, normal: 60, version: 1);
        Assert.True((await CalculateAsync(f, v1)).Succeeded);
        var first = await f.Db.PayrollResults.AsNoTracking().SingleAsync(x => x.IsCurrent);
        var v2 = v1 with { SnapshotId = Guid.NewGuid(), Version = 2, NormalDayMinutes = 120, TotalApprovedMinutes = 120 };
        Assert.True((await CalculateAsync(f, v2, recalculate: true)).Succeeded);
        var preserved = await f.Db.PayrollResults.AsNoTracking().SingleAsync(x => x.Id == first.Id);
        var current = await f.Db.PayrollResults.AsNoTracking().SingleAsync(x => x.IsCurrent);
        Assert.Equal(v1.SnapshotId, preserved.PayrollOvertimeSnapshotId);
        Assert.Equal(v2.SnapshotId, current.PayrollOvertimeSnapshotId);
        Assert.False(preserved.IsCurrent);
    }

    [Fact]
    public async Task Finalized_payroll_requires_controlled_correction()
    {
        await using var f = await Phase6BAcceptanceData.FinalizedAsync();
        Assert.True((await CalculateAsync(f, Contract(f, normal: 60))).Succeeded);
        var run = await f.Db.PayrollRuns.SingleAsync(x => x.Id == f.PayrollRunId);
        run.Status = PayrollRunStatus.Finalized;
        await f.Db.SaveChangesAsync();
        var result = await CalculateAsync(f, Contract(f, normal: 120), recalculate: true);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task No_duplicate_ot_payment_source()
    {
        await using var f = await Phase6BAcceptanceData.FinalizedAsync();
        Assert.True((await CalculateAsync(f, Contract(f, normal: 60))).Succeeded);
        var components = await CurrentOtComponents(f);
        Assert.Single(components);
        Assert.Equal("PayrollOvertimeSnapshot", components[0].CalculationSource);
        Assert.Equal(0, components.Count(x => x.CalculationSource == "PayrollInput" || x.CalculationSource == "ManualEarning"));
    }

    private static async Task<Result<PayrollCalculationSummaryDto>> CalculateAsync(Phase6BAcceptanceData f, PayrollOvertimeSnapshotContract contract, bool recalculate = false)
    {
        if (!await f.Db.SalaryComponents.AnyAsync())
            Assert.True((await f.CalculatePayrollAsync()).Succeeded);
        contract = contract with { HourlyRateComponentId = await f.Db.SalaryComponents.Select(x => (Guid?)x.Id).FirstAsync() };
        return await new PayrollCalculationEngine(f.Db, f.Scope, TimeProvider.System, attendance: new AttendancePayrollSnapshotResolver(f.Db, f.Scope), overtime: new FixedResolver(contract)).CalculateAsync(f.PayrollRunId, true);
    }

    private static async Task<Result<PayrollCalculationSummaryDto>> CalculateAsync(Phase6BAcceptanceData f, FailureResolver resolver, bool recalculate = false)
    {
        if (!await f.Db.SalaryComponents.AnyAsync())
            Assert.True((await f.CalculatePayrollAsync()).Succeeded);
        return await new PayrollCalculationEngine(f.Db, f.Scope, TimeProvider.System, attendance: new AttendancePayrollSnapshotResolver(f.Db, f.Scope), overtime: resolver).CalculateAsync(f.PayrollRunId, true);
    }

    private static async Task<List<PayrollResultComponent>> CurrentOtComponents(Phase6BAcceptanceData f) =>
        await f.Db.PayrollResultComponents.Where(x => x.PayrollResult!.IsCurrent && x.CalculationSource == "PayrollOvertimeSnapshot").ToListAsync();

    private static PayrollOvertimeSnapshotContract Contract(Phase6BAcceptanceData f, int normal = 0, int weekOff = 0, int holiday = 0, decimal normalMultiplier = 1.5m, decimal weekMultiplier = 2m, decimal holidayMultiplier = 2m, int version = 1)
    {
        var attendance = f.Db.PayrollAttendanceSnapshots.Single(x => x.IsCurrent);
        return new(Guid.NewGuid(), version, attendance.Id, attendance.Version, normal, weekOff, holiday, normal + weekOff + holiday, normalMultiplier, weekMultiplier, holidayMultiplier, null, 2400);
    }

    private sealed class FixedResolver(PayrollOvertimeSnapshotContract contract) : IPayrollOvertimeSnapshotResolver
    {
        public Task<Result<PayrollOvertimeSnapshotContract?>> ResolveAsync(Guid employeeId, DateOnly periodStart, DateOnly periodEnd, CancellationToken ct = default) => Task.FromResult(Result<PayrollOvertimeSnapshotContract?>.Success(contract));
    }

    private sealed class FailureResolver(string message) : IPayrollOvertimeSnapshotResolver
    {
        public Task<Result<PayrollOvertimeSnapshotContract?>> ResolveAsync(Guid employeeId, DateOnly periodStart, DateOnly periodEnd, CancellationToken ct = default) => Task.FromResult(Result<PayrollOvertimeSnapshotContract?>.Conflict(message));
    }
}
