using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class CompOffConcurrencyTests
{
    [Fact]
    public async Task Credit_vs_credit_same_source()
    {
        await using var fixture = await CompOffAcceptanceTests.CompOffFixture.CreateAsync(RosterDayType.WeeklyOff, 480);
        Assert.True((await fixture.Service.CreatePolicyAsync(Policy("RACE-CREDIT"))).Succeeded);
        var left = fixture.CreateIndependentService();
        var right = fixture.CreateIndependentService();
        var results = await RaceAny(
            () => left.Service.EarnAsync(new(fixture.EmployeeId, fixture.WorkDate, CompOffSourceType.WeekOff, fixture.AttendanceDayId, 1)),
            () => right.Service.EarnAsync(new(fixture.EmployeeId, fixture.WorkDate, CompOffSourceType.WeekOff, fixture.AttendanceDayId, 1)));

        AssertNoExceptions(results);
        Assert.Equal(1, await fixture.Db.CompOffEarnings.CountAsync());
        Assert.Equal(1, await fixture.Db.CompOffLedgerEntries.CountAsync(x => x.EntryType == CompOffLedgerEntryType.Credit));
        await Dispose(left, right);
    }

    [Fact]
    public async Task Approval_vs_approval()
    {
        await using var fixture = await CompOffAcceptanceTests.CompOffFixture.CreateAsync(RosterDayType.WeeklyOff, 480);
        Assert.True((await fixture.Service.CreatePolicyAsync(Policy("RACE-APPROVAL", requireApproval: true))).Succeeded);
        var pending = await fixture.Service.EarnAsync(new(fixture.EmployeeId, fixture.WorkDate, CompOffSourceType.WeekOff, fixture.AttendanceDayId, 1));
        Assert.True(pending.Succeeded, pending.Message);
        var left = fixture.CreateIndependentService();
        var right = fixture.CreateIndependentService();
        var results = await Race(() => left.Service.ApproveAsync(pending.Value!.Id), () => right.Service.ApproveAsync(pending.Value.Id));

        AssertNoExceptions(results);
        Assert.Equal(CompOffEarningStatus.Approved, await fixture.Db.CompOffEarnings.Where(x => x.Id == pending.Value.Id).Select(x => x.Status).SingleAsync());
        Assert.Equal(1, await fixture.Db.CompOffLedgerEntries.CountAsync(x => x.EntryType == CompOffLedgerEntryType.Credit));
        await Dispose(left, right);
    }

    [Fact]
    public async Task Expiry_vs_leave_reservation()
    {
        await using var fixture = await CompOffAcceptanceTests.CompOffFixture.CreateAsync(RosterDayType.WeeklyOff, 480);
        Assert.True((await fixture.Service.CreatePolicyAsync(Policy("RACE-EXPIRY", expiryDays: 1))).Succeeded);
        var earning = await fixture.Service.EarnAsync(new(fixture.EmployeeId, fixture.WorkDate, CompOffSourceType.WeekOff, fixture.AttendanceDayId, 1));
        Assert.True(earning.Succeeded, earning.Message);
        var left = fixture.CreateIndependentService();
        var right = fixture.CreateIndependentService();
        var results = await RaceAny(
            () => left.Service.ExpireAsync(fixture.WorkDate.AddDays(1)),
            () => right.Service.ReserveAsync(Guid.NewGuid(), fixture.EmployeeId, 240));

        AssertNoExceptions(results);
        var status = await fixture.Db.CompOffEarnings.Where(x => x.Id == earning.Value!.Id).Select(x => x.Status).SingleAsync();
        var balance = (await fixture.Service.GetBalanceAsync(fixture.EmployeeId)).Value!;
        Assert.True(balance.ReservedMinutes == 0 || status != CompOffEarningStatus.Expired);
        Assert.True(balance.AvailableMinutes >= 0 && balance.ReservedMinutes >= 0 && balance.ExpiredMinutes >= 0);
        await Dispose(left, right);
    }

    [Fact]
    public async Task Reservation_vs_reservation()
    {
        await using var fixture = await CompOffAcceptanceTests.CompOffFixture.CreateAsync(RosterDayType.WeeklyOff, 240);
        Assert.True((await fixture.Service.CreatePolicyAsync(Policy("RACE-RESERVE"))).Succeeded);
        var earning = await fixture.Service.EarnAsync(new(fixture.EmployeeId, fixture.WorkDate, CompOffSourceType.WeekOff, fixture.AttendanceDayId, 1));
        Assert.True(earning.Succeeded, earning.Message);
        var left = fixture.CreateIndependentService();
        var right = fixture.CreateIndependentService();
        var results = await RaceAny(
            () => left.Service.ReserveAsync(Guid.NewGuid(), fixture.EmployeeId, 240),
            () => right.Service.ReserveAsync(Guid.NewGuid(), fixture.EmployeeId, 240));

        AssertNoExceptions(results);
        var balance = (await fixture.Service.GetBalanceAsync(fixture.EmployeeId)).Value!;
        Assert.True(balance.ReservedMinutes <= 240);
        Assert.True(balance.AvailableMinutes >= 0);
        Assert.Equal(1, await fixture.Db.CompOffLeaveAllocations.CountAsync());
        await Dispose(left, right);
    }

    [Fact]
    public async Task Leave_approval_vs_leave_cancellation()
    {
        await using var fixture = await CompOffAcceptanceTests.CompOffFixture.CreateAsync(RosterDayType.WeeklyOff, 480);
        Assert.True((await fixture.Service.CreatePolicyAsync(Policy("RACE-LEAVE"))).Succeeded);
        var earning = await fixture.Service.EarnAsync(new(fixture.EmployeeId, fixture.WorkDate, CompOffSourceType.WeekOff, fixture.AttendanceDayId, 1));
        Assert.True(earning.Succeeded, earning.Message);
        var leaveId = Guid.NewGuid();
        Assert.True((await fixture.Service.ReserveAsync(leaveId, fixture.EmployeeId, 240)).Succeeded);
        var left = fixture.CreateIndependentService();
        var right = fixture.CreateIndependentService();
        var results = await Race(() => left.Service.ConsumeAsync(leaveId), () => right.Service.RestoreAsync(leaveId));

        AssertNoExceptions(results);
        Assert.True(await fixture.Db.CompOffLedgerEntries.CountAsync(x => x.EntryType == CompOffLedgerEntryType.Consume && x.LeaveRequestId == leaveId) <= 1);
        Assert.True(await fixture.Db.CompOffLedgerEntries.CountAsync(x => x.EntryType == CompOffLedgerEntryType.Restore && x.LeaveRequestId == leaveId) <= 1);
        var balance = (await fixture.Service.GetBalanceAsync(fixture.EmployeeId)).Value!;
        Assert.True(balance.AvailableMinutes >= 0 && balance.ReservedMinutes >= 0 && balance.ConsumedMinutes >= 0);
        await Dispose(left, right);
    }

    [Fact]
    public async Task Source_correction_vs_leave_reservation()
    {
        await using var fixture = await CompOffAcceptanceTests.CompOffFixture.CreateAsync(RosterDayType.WeeklyOff, 480);
        Assert.True((await fixture.Service.CreatePolicyAsync(Policy("RACE-CORRECTION"))).Succeeded);
        var earning = await fixture.Service.EarnAsync(new(fixture.EmployeeId, fixture.WorkDate, CompOffSourceType.WeekOff, fixture.AttendanceDayId, 1));
        Assert.True(earning.Succeeded, earning.Message);
        var left = fixture.CreateIndependentService();
        var right = fixture.CreateIndependentService();
        var results = await RaceAny(
            () => left.Service.CorrectSourceAsync(earning.Value!.Id, 240, "concurrent Attendance correction"),
            () => right.Service.ReserveAsync(Guid.NewGuid(), fixture.EmployeeId, 240));

        AssertNoExceptions(results);
        Assert.Contains(await fixture.Db.CompOffEarnings.AsNoTracking().Select(x => x.Id).ToListAsync(), id => id == earning.Value.Id);
        var balance = (await fixture.Service.GetBalanceAsync(fixture.EmployeeId)).Value!;
        Assert.True(balance.AvailableMinutes >= 0 && balance.ReservedMinutes >= 0 && balance.ConsumedMinutes >= 0);
        await Dispose(left, right);
    }

    [Fact]
    public async Task Credit_reversal_vs_consumption()
    {
        await using var fixture = await CompOffAcceptanceTests.CompOffFixture.CreateAsync(RosterDayType.WeeklyOff, 480);
        Assert.True((await fixture.Service.CreatePolicyAsync(Policy("RACE-REVERSAL"))).Succeeded);
        var earning = await fixture.Service.EarnAsync(new(fixture.EmployeeId, fixture.WorkDate, CompOffSourceType.WeekOff, fixture.AttendanceDayId, 1));
        Assert.True(earning.Succeeded, earning.Message);
        var leaveId = Guid.NewGuid();
        Assert.True((await fixture.Service.ReserveAsync(leaveId, fixture.EmployeeId, 240)).Succeeded);
        var left = fixture.CreateIndependentService();
        var right = fixture.CreateIndependentService();
        var results = await RaceAny(
            () => left.Service.CorrectSourceAsync(earning.Value!.Id, 120, "concurrent reversal"),
            () => right.Service.ConsumeAsync(leaveId));

        AssertNoExceptions(results);
        var balance = (await fixture.Service.GetBalanceAsync(fixture.EmployeeId)).Value!;
        Assert.True(balance.AvailableMinutes >= 0 && balance.ReservedMinutes >= 0 && balance.ConsumedMinutes >= 0);
        Assert.True(await fixture.Db.CompOffLedgerEntries.CountAsync(x => x.EntryType == CompOffLedgerEntryType.Reversal) <= 1);
        await Dispose(left, right);
    }

    [Fact]
    public async Task Duplicate_ledger_entry_race()
    {
        await using var fixture = await CompOffAcceptanceTests.CompOffFixture.CreateAsync(RosterDayType.WeeklyOff, 480);
        Assert.True((await fixture.Service.CreatePolicyAsync(Policy("RACE-IDEMPOTENCY"))).Succeeded);
        var earning = await fixture.Service.EarnAsync(new(fixture.EmployeeId, fixture.WorkDate, CompOffSourceType.WeekOff, fixture.AttendanceDayId, 1));
        Assert.True(earning.Succeeded, earning.Message);
        var leaveId = Guid.NewGuid();
        var left = fixture.CreateIndependentService();
        var right = fixture.CreateIndependentService();
        var results = await Race(() => left.Service.ReserveAsync(leaveId, fixture.EmployeeId, 240), () => right.Service.ReserveAsync(leaveId, fixture.EmployeeId, 240));

        AssertNoExceptions(results);
        Assert.Equal(1, await fixture.Db.CompOffLeaveAllocations.CountAsync(x => x.LeaveRequestId == leaveId));
        Assert.Equal(1, await fixture.Db.CompOffLedgerEntries.CountAsync(x => x.LeaveRequestId == leaveId && x.EntryType == CompOffLedgerEntryType.Reserve));
        await Dispose(left, right);
    }

    private static CompOffPolicyRequest Policy(string code, bool requireApproval = false, int? expiryDays = null) =>
        new(code, code, new(2026, 1, 1), null, true, true, false, 0, 1m, CompOffRoundingMode.None, 0, null, null, expiryDays, null, requireApproval);

    private static async Task<(OperationResult<T> Left, OperationResult<T> Right)> Race<T>(Func<Task<Result<T>>> left, Func<Task<Result<T>>> right)
    {
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = Capture(gate.Task, left);
        var second = Capture(gate.Task, right);
        gate.SetResult(true);
        return (await first, await second);
    }

    private static async Task<(Exception? Left, Exception? Right)> RaceAny(Func<Task> left, Func<Task> right)
    {
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = CaptureAny(gate.Task, left);
        var second = CaptureAny(gate.Task, right);
        gate.SetResult(true);
        return (await first, await second);
    }

    private static async Task<Exception?> CaptureAny(Task gate, Func<Task> operation)
    {
        await gate;
        try { await operation(); return null; }
        catch (Exception ex) { return ex; }
    }

    private static void AssertNoExceptions((Exception? Left, Exception? Right) results)
    {
        Assert.Null(results.Left);
        Assert.Null(results.Right);
    }

    private static async Task<OperationResult<T>> Capture<T>(Task gate, Func<Task<Result<T>>> operation)
    {
        await gate;
        try { return new(await operation(), null); }
        catch (Exception ex) { return new(null, ex); }
    }

    private static void AssertNoExceptions<T>((OperationResult<T> Left, OperationResult<T> Right) results)
    {
        Assert.Null(results.Left.Exception);
        Assert.Null(results.Right.Exception);
    }

    private static async Task Dispose((HRMS.Infrastructure.Persistence.HrmsDbContext Db, ICompOffService Service) left, (HRMS.Infrastructure.Persistence.HrmsDbContext Db, ICompOffService Service) right)
    {
        await left.Db.DisposeAsync();
        await right.Db.DisposeAsync();
    }

    private sealed record OperationResult<T>(Result<T>? Value, Exception? Exception);
}
