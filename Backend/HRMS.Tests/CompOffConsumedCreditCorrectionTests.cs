using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class CompOffConsumedCreditCorrectionTests
{
    [Fact]
    public async Task Consumed_credit_reduction_requires_idempotent_operational_adjustment()
    {
        await using var fixture = await CompOffAcceptanceTests.CompOffFixture.CreateAsync(RosterDayType.WeeklyOff, 480);
        Assert.True((await fixture.Service.CreatePolicyAsync(fixture.Policy("COR-USED", new(2026, 1, 1), null, ratio: 1))).Succeeded);
        var earning = await fixture.Service.EarnAsync(new(fixture.EmployeeId, fixture.WorkDate, CompOffSourceType.WeekOff, fixture.AttendanceDayId, 1));
        Assert.True(earning.Succeeded, earning.Message);
        var leave = Guid.NewGuid();
        Assert.True((await fixture.Service.ReserveAsync(leave, fixture.EmployeeId, 240)).Succeeded);
        Assert.True((await fixture.Service.ConsumeAsync(leave)).Succeeded);
        var before = await fixture.Db.CompOffLedgerEntries.CountAsync();

        var first = await fixture.Service.CorrectSourceAsync(earning.Value!.Id, 120, "Attendance V2 reduction after consumption");
        var second = await fixture.Service.CorrectSourceAsync(earning.Value.Id, 120, "Attendance V2 reduction after consumption retry");

        Assert.False(first.Succeeded);
        Assert.Contains("CorrectionRequiresAdjustment", first.Message);
        Assert.False(second.Succeeded);
        Assert.Contains("CorrectionRequiresAdjustment", second.Message);
        Assert.Equal(before, await fixture.Db.CompOffLedgerEntries.CountAsync());
        Assert.Equal(240, (await fixture.Service.GetBalanceAsync(fixture.EmployeeId)).Value!.ConsumedMinutes);
        Assert.Equal(240, (await fixture.Service.GetBalanceAsync(fixture.EmployeeId)).Value!.AvailableMinutes);
    }
}
