using HRMS.Domain.Enums;

namespace HRMS.Tests;

public sealed class CompOffBalanceInvariantTests
{
    [Fact]
    public async Task Complete_ledger_lifecycle_preserves_non_negative_balances()
    {
        await using var fixture = await CompOffAcceptanceTests.CompOffFixture.CreateAsync(RosterDayType.WeeklyOff, 480);
        Assert.True((await fixture.Service.CreatePolicyAsync(fixture.Policy("BAL", new(2026, 1, 1), null, ratio: 1, expiryDays: 100))).Succeeded);
        var earning = await fixture.Service.EarnAsync(new(fixture.EmployeeId, fixture.WorkDate, CompOffSourceType.WeekOff, fixture.AttendanceDayId, 1));
        Assert.True(earning.Succeeded, earning.Message);
        var leave = Guid.NewGuid();
        Assert.True((await fixture.Service.ReserveAsync(leave, fixture.EmployeeId, 240)).Succeeded);
        Assert.True((await fixture.Service.ConsumeAsync(leave)).Succeeded);
        Assert.True((await fixture.Service.ConsumeAsync(leave)).Succeeded);
        Assert.True((await fixture.Service.RestoreAsync(leave)).Succeeded);
        Assert.True((await fixture.Service.RestoreAsync(leave)).Succeeded);

        var balance = (await fixture.Service.GetBalanceAsync(fixture.EmployeeId)).Value!;
        Assert.True(balance.AvailableMinutes >= 0);
        Assert.True(balance.ReservedMinutes >= 0);
        Assert.True(balance.ConsumedMinutes >= 0);
        Assert.True(balance.ExpiredMinutes >= 0);
        Assert.Equal(480, balance.AvailableMinutes);
        Assert.Equal(0, balance.ReservedMinutes);
        Assert.Equal(0, balance.ConsumedMinutes);
    }

    [Fact]
    public async Task Overconsumption_and_duplicate_transitions_do_not_change_entitlement_twice()
    {
        await using var fixture = await CompOffAcceptanceTests.CompOffFixture.CreateAsync(RosterDayType.WeeklyOff, 480);
        Assert.True((await fixture.Service.CreatePolicyAsync(fixture.Policy("LIMIT", new(2026, 1, 1), null, ratio: 1))).Succeeded);
        var earning = await fixture.Service.EarnAsync(new(fixture.EmployeeId, fixture.WorkDate, CompOffSourceType.WeekOff, fixture.AttendanceDayId, 1));
        Assert.True(earning.Succeeded, earning.Message);
        Assert.False((await fixture.Service.ReserveAsync(Guid.NewGuid(), fixture.EmployeeId, 600)).Succeeded);
        var leave = Guid.NewGuid();
        Assert.True((await fixture.Service.ReserveAsync(leave, fixture.EmployeeId, 240)).Succeeded);
        Assert.True((await fixture.Service.ConsumeAsync(leave)).Succeeded);
        Assert.True((await fixture.Service.ConsumeAsync(leave)).Succeeded);
        var balance = (await fixture.Service.GetBalanceAsync(fixture.EmployeeId)).Value!;
        Assert.Equal(240, balance.AvailableMinutes);
        Assert.Equal(240, balance.ConsumedMinutes);
        Assert.True(balance.AvailableMinutes >= 0);
        Assert.True(balance.ReservedMinutes >= 0);
        Assert.True(balance.ConsumedMinutes >= 0);
        Assert.True(balance.ExpiredMinutes >= 0);
    }
}
