using HRMS.Application.Abstractions;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HRMS.Tests;

public sealed class CompOffFailureInjectionTests
{
    [Fact]
    public async Task Credit_persistence_failure_retry_safe()
    {
        await using var fixture = await CompOffAcceptanceTests.CompOffFixture.CreateAsync(RosterDayType.WeeklyOff, 480);
        Assert.True((await fixture.Service.CreatePolicyAsync(Policy("FAIL-CREDIT"))).Succeeded);
        var failing = fixture.CreateIndependentService(new ThrowOnceOnSave());
        await Assert.ThrowsAsync<InvalidOperationException>(() => failing.Service.EarnAsync(new(fixture.EmployeeId, fixture.WorkDate, CompOffSourceType.WeekOff, fixture.AttendanceDayId, 1)));
        Assert.Equal(0, await fixture.Db.CompOffEarnings.CountAsync());
        Assert.Equal(0, await fixture.Db.CompOffLedgerEntries.CountAsync());
        await failing.Db.DisposeAsync();
        var retry = fixture.CreateIndependentService();
        var result = await retry.Service.EarnAsync(new(fixture.EmployeeId, fixture.WorkDate, CompOffSourceType.WeekOff, fixture.AttendanceDayId, 1));
        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(1, await fixture.Db.CompOffEarnings.CountAsync());
        Assert.Equal(1, await fixture.Db.CompOffLedgerEntries.CountAsync(x => x.EntryType == CompOffLedgerEntryType.Credit));
        await retry.Db.DisposeAsync();
    }

    [Fact]
    public async Task Ledger_write_failure_does_not_change_balance()
    {
        await using var fixture = await CompOffAcceptanceTests.CompOffFixture.CreateAsync(RosterDayType.WeeklyOff, 480);
        Assert.True((await fixture.Service.CreatePolicyAsync(Policy("FAIL-LEDGER"))).Succeeded);
        var before = (await fixture.Service.GetBalanceAsync(fixture.EmployeeId)).Value!;
        var failing = fixture.CreateIndependentService(new ThrowOnceOnSave());
        await Assert.ThrowsAsync<InvalidOperationException>(() => failing.Service.EarnAsync(new(fixture.EmployeeId, fixture.WorkDate, CompOffSourceType.WeekOff, fixture.AttendanceDayId, 1)));
        await failing.Db.DisposeAsync();
        var after = (await fixture.Service.GetBalanceAsync(fixture.EmployeeId)).Value!;
        Assert.Equal(before.AvailableMinutes, after.AvailableMinutes);
        Assert.Equal(before.ReservedMinutes, after.ReservedMinutes);
        Assert.Equal(0, await fixture.Db.CompOffLedgerEntries.CountAsync());
    }

    [Fact]
    public async Task Reservation_failure_does_not_consume_credit()
    {
        await using var fixture = await CompOffAcceptanceTests.CompOffFixture.CreateAsync(RosterDayType.WeeklyOff, 480);
        Assert.True((await fixture.Service.CreatePolicyAsync(Policy("FAIL-RESERVE"))).Succeeded);
        var earning = await fixture.Service.EarnAsync(new(fixture.EmployeeId, fixture.WorkDate, CompOffSourceType.WeekOff, fixture.AttendanceDayId, 1));
        Assert.True(earning.Succeeded, earning.Message);
        var before = (await fixture.Service.GetBalanceAsync(fixture.EmployeeId)).Value!;
        var failing = fixture.CreateIndependentService(new ThrowOnceOnSave());
        await Assert.ThrowsAsync<InvalidOperationException>(() => failing.Service.ReserveAsync(Guid.NewGuid(), fixture.EmployeeId, 240));
        await failing.Db.DisposeAsync();
        var after = (await fixture.Service.GetBalanceAsync(fixture.EmployeeId)).Value!;
        Assert.Equal(before.AvailableMinutes, after.AvailableMinutes);
        Assert.Equal(before.ReservedMinutes, after.ReservedMinutes);
        Assert.Equal(0, after.ConsumedMinutes);
        Assert.Equal(1, await fixture.Db.CompOffLedgerEntries.CountAsync(x => x.EntryType == CompOffLedgerEntryType.Credit));
    }

    [Fact]
    public async Task Leave_approval_failure_does_not_partially_consume()
    {
        await using var fixture = await CompOffAcceptanceTests.CompOffFixture.CreateAsync(RosterDayType.WeeklyOff, 480);
        Assert.True((await fixture.Service.CreatePolicyAsync(Policy("FAIL-APPROVAL"))).Succeeded);
        var earning = await fixture.Service.EarnAsync(new(fixture.EmployeeId, fixture.WorkDate, CompOffSourceType.WeekOff, fixture.AttendanceDayId, 1));
        Assert.True(earning.Succeeded, earning.Message);
        var secondDate = new DateOnly(2026, 9, 16);
        var secondDay = await fixture.AddDayAsync(secondDate, 480);
        var second = await fixture.Service.EarnAsync(new(fixture.EmployeeId, secondDate, CompOffSourceType.WeekOff, secondDay, 1));
        Assert.True(second.Succeeded, second.Message);
        var leaveId = Guid.NewGuid();
        Assert.True((await fixture.Service.ReserveAsync(leaveId, fixture.EmployeeId, 600)).Succeeded);
        var failing = fixture.CreateIndependentService(new ThrowOnceOnSave());
        await Assert.ThrowsAsync<InvalidOperationException>(() => failing.Service.ConsumeAsync(leaveId));
        await failing.Db.DisposeAsync();
        Assert.Equal(0, await fixture.Db.CompOffLedgerEntries.CountAsync(x => x.EntryType == CompOffLedgerEntryType.Consume));
        Assert.Equal(600, (await fixture.Service.GetBalanceAsync(fixture.EmployeeId)).Value!.ReservedMinutes);
        var retry = fixture.CreateIndependentService();
        Assert.True((await retry.Service.ConsumeAsync(leaveId)).Succeeded);
        Assert.Equal(600, (await fixture.Service.GetBalanceAsync(fixture.EmployeeId)).Value!.ConsumedMinutes);
        Assert.Equal(0, (await fixture.Service.GetBalanceAsync(fixture.EmployeeId)).Value!.ReservedMinutes);
        await retry.Db.DisposeAsync();
    }

    [Fact]
    public async Task Expiry_failure_retry_safe()
    {
        await using var fixture = await CompOffAcceptanceTests.CompOffFixture.CreateAsync(RosterDayType.WeeklyOff, 480);
        Assert.True((await fixture.Service.CreatePolicyAsync(Policy("FAIL-EXPIRY", expiryDays: 1))).Succeeded);
        var earning = await fixture.Service.EarnAsync(new(fixture.EmployeeId, fixture.WorkDate, CompOffSourceType.WeekOff, fixture.AttendanceDayId, 1));
        Assert.True(earning.Succeeded, earning.Message);
        var failing = fixture.CreateIndependentService(new ThrowOnceOnSave());
        await Assert.ThrowsAsync<InvalidOperationException>(() => failing.Service.ExpireAsync(fixture.WorkDate.AddDays(1)));
        await failing.Db.DisposeAsync();
        Assert.Equal(0, await fixture.Db.CompOffLedgerEntries.CountAsync(x => x.EntryType == CompOffLedgerEntryType.Expire));
        var retry = fixture.CreateIndependentService();
        Assert.Equal(1, (await retry.Service.ExpireAsync(fixture.WorkDate.AddDays(1))).Value);
        Assert.Equal(1, await fixture.Db.CompOffLedgerEntries.CountAsync(x => x.EntryType == CompOffLedgerEntryType.Expire));
        Assert.Equal(1, await fixture.Db.CompOffEarnings.CountAsync(x => x.Status == CompOffEarningStatus.Expired));
        await retry.Db.DisposeAsync();
    }

    [Fact]
    public async Task Restoration_failure_does_not_double_restore()
    {
        await using var fixture = await CompOffAcceptanceTests.CompOffFixture.CreateAsync(RosterDayType.WeeklyOff, 480);
        Assert.True((await fixture.Service.CreatePolicyAsync(Policy("FAIL-RESTORE"))).Succeeded);
        var earning = await fixture.Service.EarnAsync(new(fixture.EmployeeId, fixture.WorkDate, CompOffSourceType.WeekOff, fixture.AttendanceDayId, 1));
        Assert.True(earning.Succeeded, earning.Message);
        var leaveId = Guid.NewGuid();
        Assert.True((await fixture.Service.ReserveAsync(leaveId, fixture.EmployeeId, 240)).Succeeded);
        Assert.True((await fixture.Service.ConsumeAsync(leaveId)).Succeeded);
        var failing = fixture.CreateIndependentService(new ThrowOnceOnSave());
        await Assert.ThrowsAsync<InvalidOperationException>(() => failing.Service.RestoreAsync(leaveId));
        await failing.Db.DisposeAsync();
        Assert.Equal(0, await fixture.Db.CompOffLedgerEntries.CountAsync(x => x.EntryType == CompOffLedgerEntryType.Restore));
        var retry = fixture.CreateIndependentService();
        Assert.True((await retry.Service.RestoreAsync(leaveId)).Succeeded);
        Assert.True((await retry.Service.RestoreAsync(leaveId)).Succeeded);
        Assert.Equal(1, await fixture.Db.CompOffLedgerEntries.CountAsync(x => x.EntryType == CompOffLedgerEntryType.Restore));
        Assert.Equal(0, (await fixture.Service.GetBalanceAsync(fixture.EmployeeId)).Value!.ConsumedMinutes);
        await retry.Db.DisposeAsync();
    }

    private static CompOffPolicyRequest Policy(string code, int? expiryDays = null) =>
        new(code, code, new(2026, 1, 1), null, true, true, false, 0, 1m, CompOffRoundingMode.None, 0, null, null, expiryDays, null, false);

    private sealed class ThrowOnceOnSave : SaveChangesInterceptor
    {
        private int saves;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref saves) == 1)
                throw new InvalidOperationException("Injected Comp-Off persistence failure.");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
