using HRMS.Application.Abstractions;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class CompOffOvertimeExclusivityTests
{
    [Fact]
    public async Task Monetary_ot_source_blocks_comp_off_when_dual_benefit_disallowed()
    {
        await using var fixture = await CompOffAcceptanceTests.CompOffFixture.CreateAsync(RosterDayType.WeeklyOff, 480);
        Assert.True((await fixture.Service.CreatePolicyAsync(CompOffPolicy("CO-OT-BLOCK", allowOvertime: true))).Succeeded);

        var before = await fixture.Db.CompOffEarnings.CountAsync();
        var result = await fixture.Service.EarnAsync(new(fixture.EmployeeId, fixture.WorkDate, CompOffSourceType.Overtime, fixture.AttendanceDayId, 1, 480, Guid.NewGuid(), Guid.NewGuid()));

        Assert.False(result.Succeeded);
        Assert.Contains("DoubleBenefitDenied", result.Message);
        Assert.Equal(before, await fixture.Db.CompOffEarnings.CountAsync());
        Assert.Equal(0, await fixture.Db.CompOffLedgerEntries.CountAsync());
    }

    [Fact]
    public async Task Comp_off_source_blocks_monetary_ot_when_dual_benefit_disallowed()
    {
        await using var fixture = await CompOffAcceptanceTests.CompOffFixture.CreateAsync(RosterDayType.WeeklyOff, 480);
        Assert.True((await fixture.Service.CreatePolicyAsync(CompOffPolicy("CO-OT-RECIPROCAL", allowOvertime: false))).Succeeded);
        var earning = await fixture.Service.EarnAsync(new(fixture.EmployeeId, fixture.WorkDate, CompOffSourceType.WeekOff, fixture.AttendanceDayId, 1));
        Assert.True(earning.Succeeded, earning.Message);
        var ledgerBefore = await fixture.Db.CompOffLedgerEntries.AsNoTracking().CountAsync();
        var overtime = new OvertimeService(fixture.Db, new TestTenantContext(fixture.TenantId), TimeProvider.System);
        Assert.True((await overtime.CreatePolicyAsync(OvertimePolicy("OT-CO-RECIPROCAL"))).Succeeded);

        var request = await overtime.CreateRequestAsync(new(fixture.EmployeeId, fixture.WorkDate, 60, OvertimeCategory.WeekOff, "conflicting source"));

        Assert.False(request.Succeeded);
        Assert.Contains("DoubleBenefitDenied", request.Message);
        Assert.Equal(0, await fixture.Db.OvertimeRequests.CountAsync());
        var unchanged = await fixture.Db.CompOffEarnings.AsNoTracking().SingleAsync(x => x.Id == earning.Value!.Id);
        Assert.Equal(earning.Value.CreditedMinutes, unchanged.CreditedMinutes);
        Assert.Equal(ledgerBefore, await fixture.Db.CompOffLedgerEntries.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task Different_source_events_do_not_conflict()
    {
        await using var fixture = await CompOffAcceptanceTests.CompOffFixture.CreateAsync(RosterDayType.WeeklyOff, 480);
        Assert.True((await fixture.Service.CreatePolicyAsync(CompOffPolicy("CO-DISTINCT", allowOvertime: false))).Succeeded);
        var compOff = await fixture.Service.EarnAsync(new(fixture.EmployeeId, fixture.WorkDate, CompOffSourceType.WeekOff, fixture.AttendanceDayId, 1));
        Assert.True(compOff.Succeeded, compOff.Message);
        var otherDate = new DateOnly(2026, 9, 16);
        var otherDayId = await fixture.AddDayAsync(otherDate, 540);
        var overtime = new OvertimeService(fixture.Db, new TestTenantContext(fixture.TenantId), TimeProvider.System);
        Assert.True((await overtime.CreatePolicyAsync(OvertimePolicy("OT-DISTINCT"))).Succeeded);

        var request = await overtime.CreateRequestAsync(new(fixture.EmployeeId, otherDate, 60, OvertimeCategory.WeekOff, "independent source"));

        Assert.True(request.Succeeded, request.Message);
        Assert.Equal(otherDate, request.Value!.WorkDate);
        Assert.Equal(1, await fixture.Db.CompOffEarnings.CountAsync(x => x.SourceAttendanceDayId == fixture.AttendanceDayId));
        Assert.Equal(1, await fixture.Db.OvertimeRequests.CountAsync(x => x.WorkDate == otherDate));
        Assert.NotEqual(fixture.AttendanceDayId, otherDayId);
    }

    [Fact]
    public async Task Historical_finalized_ot_is_not_mutated_when_comp_off_rejected()
    {
        await using var fixture = await OvertimeAcceptanceTests.OvertimeFixture.CreateAsync();
        Assert.True((await fixture.Service.CreatePolicyAsync(fixture.PolicyRequest())).Succeeded);
        var request = await fixture.Service.CreateRequestAsync(new(fixture.EmployeeId, fixture.WorkDate, 60, OvertimeCategory.NormalDay, "finalized source"));
        Assert.True(request.Succeeded, request.Message);
        Assert.True((await fixture.Service.SubmitAsync(request.Value!.Id)).Succeeded);
        Assert.True((await fixture.Service.ApproveAsync(request.Value.Id, new())).Succeeded);
        var finalized = await fixture.Service.FinalizeAsync(fixture.PeriodId);
        Assert.True(finalized.Succeeded, finalized.Message);
        var snapshotBefore = await fixture.Db.PayrollOvertimeSnapshots.AsNoTracking().SingleAsync(x => x.IsCurrent);
        var day = await fixture.Db.EmployeeAttendanceDays.AsNoTracking().SingleAsync(x => x.EmployeeId == fixture.EmployeeId && x.BusinessDate == fixture.WorkDate);
        var compOff = new CompOffService(fixture.Db, new TestTenantContext(fixture.TenantId), TimeProvider.System);
        Assert.True((await compOff.CreatePolicyAsync(CompOffPolicy("CO-HIST-OT", allowOvertime: true))).Succeeded);

        var rejected = await compOff.EarnAsync(new(fixture.EmployeeId, fixture.WorkDate, CompOffSourceType.Overtime, day.Id, 1, 480, request.Value.Id, snapshotBefore.Id));

        Assert.False(rejected.Succeeded);
        Assert.Contains("DoubleBenefitDenied", rejected.Message);
        var snapshotAfter = await fixture.Db.PayrollOvertimeSnapshots.AsNoTracking().SingleAsync(x => x.Id == snapshotBefore.Id);
        Assert.Equal(snapshotBefore.Id, snapshotAfter.Id);
        Assert.Equal(snapshotBefore.Version, snapshotAfter.Version);
        Assert.Equal(snapshotBefore.TotalApprovedMinutes, snapshotAfter.TotalApprovedMinutes);
        Assert.Equal(snapshotBefore.NormalDayMinutes, snapshotAfter.NormalDayMinutes);
        Assert.Equal(snapshotBefore.WeekOffMinutes, snapshotAfter.WeekOffMinutes);
        Assert.Equal(snapshotBefore.HolidayMinutes, snapshotAfter.HolidayMinutes);
        Assert.Equal(snapshotBefore.AttendanceSnapshotId, snapshotAfter.AttendanceSnapshotId);
        Assert.Equal(snapshotBefore.AttendanceVersion, snapshotAfter.AttendanceVersion);
        Assert.Equal(snapshotBefore.NormalDayMultiplier, snapshotAfter.NormalDayMultiplier);
        Assert.Equal(snapshotBefore.WeekOffMultiplier, snapshotAfter.WeekOffMultiplier);
        Assert.Equal(snapshotBefore.HolidayMultiplier, snapshotAfter.HolidayMultiplier);
        Assert.Equal(0, await fixture.Db.CompOffEarnings.CountAsync());
    }

    [Fact]
    public async Task Historical_comp_off_is_not_mutated_when_ot_rejected()
    {
        await using var fixture = await CompOffAcceptanceTests.CompOffFixture.CreateAsync(RosterDayType.WeeklyOff, 480);
        Assert.True((await fixture.Service.CreatePolicyAsync(CompOffPolicy("CO-HIST-CO", allowOvertime: false))).Succeeded);
        var earning = await fixture.Service.EarnAsync(new(fixture.EmployeeId, fixture.WorkDate, CompOffSourceType.WeekOff, fixture.AttendanceDayId, 1));
        Assert.True(earning.Succeeded, earning.Message);
        var earningBefore = await fixture.Db.CompOffEarnings.AsNoTracking().SingleAsync(x => x.Id == earning.Value!.Id);
        var ledgerBefore = await fixture.Db.CompOffLedgerEntries.AsNoTracking().Where(x => x.EarningId == earning.Value.Id).OrderBy(x => x.Id).ToListAsync();
        var overtime = new OvertimeService(fixture.Db, new TestTenantContext(fixture.TenantId), TimeProvider.System);
        Assert.True((await overtime.CreatePolicyAsync(OvertimePolicy("OT-HIST-CO"))).Succeeded);

        var rejected = await overtime.CreateRequestAsync(new(fixture.EmployeeId, fixture.WorkDate, 60, OvertimeCategory.WeekOff, "conflicting source"));

        Assert.False(rejected.Succeeded);
        Assert.Contains("DoubleBenefitDenied", rejected.Message);
        var earningAfter = await fixture.Db.CompOffEarnings.AsNoTracking().SingleAsync(x => x.Id == earning.Value.Id);
        var ledgerAfter = await fixture.Db.CompOffLedgerEntries.AsNoTracking().Where(x => x.EarningId == earning.Value.Id).OrderBy(x => x.Id).ToListAsync();
        Assert.Equal(earningBefore.Id, earningAfter.Id);
        Assert.Equal(earningBefore.CreditedMinutes, earningAfter.CreditedMinutes);
        Assert.Equal(earningBefore.Status, earningAfter.Status);
        Assert.Equal(earningBefore.ExpiresOn, earningAfter.ExpiresOn);
        Assert.Equal(earningBefore.SourceAttendanceDayId, earningAfter.SourceAttendanceDayId);
        Assert.Equal(earningBefore.SourceAttendanceVersion, earningAfter.SourceAttendanceVersion);
        Assert.Equal(ledgerBefore.Select(x => (x.EntryType, x.Minutes, x.IdempotencyKey)), ledgerAfter.Select(x => (x.EntryType, x.Minutes, x.IdempotencyKey)));
        Assert.Equal(0, await fixture.Db.OvertimeRequests.CountAsync());
    }

    private static CompOffPolicyRequest CompOffPolicy(string code, bool allowOvertime) =>
        new(code, code, new(2026, 1, 1), null, true, true, allowOvertime, 0, 1m, CompOffRoundingMode.None, 0, null, null, null, null, false, true, 1, CompOffBenefitMode.CompOffOnly, "All");

    private static OvertimePolicyRequest OvertimePolicy(string code) =>
        new(code, code, new(2026, 1, 1), null, 0, 0, OvertimeRoundingMode.None, null, null, false, true, true, true, true, 1.5m, 2m, 2m, "All");
}
