using HRMS.Application.Abstractions;
using HRMS.Application.Services;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class OvertimePayrollCorrectionEndToEndTests
{
    [Fact]
    public async Task Correction_preserves_v1_requires_explicit_recalculation_and_binds_v2()
    {
        await using var f = await Phase6BAcceptanceData.FinalizedAsync();
        var rateComponent = Guid.NewGuid();
        f.Db.SalaryComponents.Add(new HRMS.Domain.Entities.SalaryComponent
        {
            Id = rateComponent,
            TenantId = f.TenantId,
            Code = "CORRECTION-BASIC",
            Name = "Correction Basic",
            ComponentType = SalaryComponentType.Earning,
            CalculationType = SalaryCalculationType.FixedAmount,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            IsActive = true,
            AffectsGross = true,
            AffectsNetPay = true
        });
        await f.Db.SaveChangesAsync();
        var service = new OvertimeService(f.Db, f.Scope, TimeProvider.System);
        var day = await f.Db.EmployeeAttendanceDays.SingleAsync(x => x.EmployeeId == f.EmployeeId && x.BusinessDate == new DateOnly(2026, 9, 15));
        day.WorkedMinutes = 540;
        await f.Db.SaveChangesAsync();
        var policy = await service.CreatePolicyAsync(new("CORRECTION-OT", "Correction OT", new(2026, 1, 1), null, 0, 0, OvertimeRoundingMode.None, null, null, false, true, true, true, true, 1.5m, 2m, 2m, "All", rateComponent, 2400));
        Assert.True(policy.Succeeded, policy.Message);
        var request = await service.CreateRequestAsync(new(f.EmployeeId, new(2026, 9, 15), 60, OvertimeCategory.NormalDay, "v1"));
        Assert.True(request.Succeeded, request.Message);
        Assert.True((await service.SubmitAsync(request.Value!.Id)).Succeeded);
        Assert.True((await service.ApproveAsync(request.Value.Id, new())).Succeeded);
        var v1Result = await service.FinalizeAsync(f.PeriodId);
        Assert.True(v1Result.Succeeded, v1Result.Message);
        var v1 = v1Result.Value!.Single();
        Assert.True((await f.CalculatePayrollAsync()).Succeeded);
        var payrollV1 = await CalculateAsync(f, service, true);
        Assert.True(payrollV1.Succeeded, payrollV1.Message);
        var oldPayroll = await f.Db.PayrollResults.AsNoTracking().SingleAsync(x => x.IsCurrent);
        Assert.Equal(v1.SnapshotId, oldPayroll.PayrollOvertimeSnapshotId);
        Assert.Equal(1, oldPayroll.OvertimeVersion);

        Assert.True((await service.ReopenAsync(f.PeriodId, "approved OT correction")).Succeeded);
        var corrected = await service.CorrectAsync(request.Value.Id, new(30, "corrected approved minutes"));
        Assert.True(corrected.Succeeded, corrected.Message);
        Assert.Equal(30, corrected.Value!.ApprovedMinutes);
        var v2Result = await service.FinalizeAsync(f.PeriodId);
        Assert.True(v2Result.Succeeded, v2Result.Message);
        var v2 = v2Result.Value!.Single();

        var snapshots = await f.Db.PayrollOvertimeSnapshots.AsNoTracking().OrderBy(x => x.Version).ToListAsync();
        Assert.Equal(2, snapshots.Count);
        Assert.Equal(v1.SnapshotId, snapshots[0].Id);
        Assert.Equal(1, snapshots[0].Version);
        Assert.Equal("approved OT correction", snapshots[0].ReopenReason);
        Assert.Equal(v2.SnapshotId, snapshots[1].Id);
        Assert.Equal(2, snapshots[1].Version);
        Assert.True(snapshots[1].IsCurrent);
        Assert.Equal(1, await f.Db.PayrollOvertimeSnapshots.CountAsync(x => x.IsCurrent));

        var unchanged = await f.Db.PayrollResults.AsNoTracking().SingleAsync(x => x.Id == oldPayroll.Id);
        Assert.Equal(v1.SnapshotId, unchanged.PayrollOvertimeSnapshotId);
        Assert.Equal(1, unchanged.OvertimeVersion);
        var recalculated = await CalculateAsync(f, service, true);
        Assert.True(recalculated.Succeeded, recalculated.Message);
        var current = await f.Db.PayrollResults.AsNoTracking().SingleAsync(x => x.IsCurrent);
        Assert.Equal(v2.SnapshotId, current.PayrollOvertimeSnapshotId);
        Assert.Equal(2, current.OvertimeVersion);
        Assert.False(await f.Db.PayrollResults.Where(x => x.Id == oldPayroll.Id).Select(x => x.IsCurrent).SingleAsync());
    }

    private static Task<HRMS.Application.Common.Result<HRMS.Application.DTOs.Payroll.PayrollCalculationSummaryDto>> CalculateAsync(Phase6BAcceptanceData f, IOvertimeService overtime, bool recalculate = false) =>
        new PayrollCalculationEngine(f.Db, f.Scope, TimeProvider.System, attendance: new AttendancePayrollSnapshotResolver(f.Db, f.Scope), overtime: overtime).CalculateAsync(f.PayrollRunId, recalculate);
}
