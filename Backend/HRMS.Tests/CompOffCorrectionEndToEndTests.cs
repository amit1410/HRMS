using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class CompOffCorrectionEndToEndTests
{
    [Fact]
    public async Task Attendance_v1_to_v2_correction_preserves_history_and_corrects_entitlement()
    {
        await using var fixture = await CompOffAcceptanceTests.CompOffFixture.CreateAsync(RosterDayType.WeeklyOff, 240);
        Assert.True((await fixture.Service.CreatePolicyAsync(fixture.Policy("COR-E2E", new(2026, 1, 1), null, ratio: 1))).Succeeded);
        var v1 = await fixture.Service.EarnAsync(new(fixture.EmployeeId, fixture.WorkDate, CompOffSourceType.WeekOff, fixture.AttendanceDayId, 1));
        Assert.True(v1.Succeeded, v1.Message);

        var v2 = await fixture.Service.CorrectSourceAsync(v1.Value!.Id, 480, "Attendance V2 increase");

        Assert.True(v2.Succeeded, v2.Message);
        Assert.Equal(CompOffEarningStatus.Superseded, await fixture.Db.CompOffEarnings.Where(x => x.Id == v1.Value.Id).Select(x => x.Status).SingleAsync());
        Assert.Equal(480, v2.Value!.CreditedMinutes);
        Assert.Equal(480, (await fixture.Service.GetBalanceAsync(fixture.EmployeeId)).Value!.AvailableMinutes);
        Assert.Contains((await fixture.Service.GetLedgerAsync(fixture.EmployeeId)).Value!, x => x.EntryType == CompOffLedgerEntryType.Credit && x.Minutes == 240);
    }
}
