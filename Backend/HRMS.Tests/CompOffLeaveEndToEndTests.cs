using HRMS.Domain.Enums;

namespace HRMS.Tests;

public sealed class CompOffLeaveEndToEndTests
{
    [Fact]
    public async Task Attendance_to_comp_off_to_leave_preserves_authoritative_traceability()
    {
        await using var fixture = await CompOffLeaveIntegrationTests.LeaveFixture.CreateAsync();
        var availableBeforeLeave = await fixture.BalanceAsync();
        var submitted = await fixture.SubmitAsync();
        Assert.True(submitted.Succeeded, submitted.Message);

        var beforeApproval = await fixture.BalanceAsync();
        var approved = await fixture.ApproveAsync();
        var after = await fixture.BalanceAsync();
        var ledger = await fixture.LedgerAsync();

        Assert.True(approved.Succeeded, approved.Message);
        Assert.Equal(480, availableBeforeLeave.AvailableMinutes);
        Assert.Equal(240, beforeApproval.ReservedMinutes);
        Assert.Equal(240, after.ConsumedMinutes);
        Assert.Equal(240, after.AvailableMinutes);
        Assert.Contains(ledger, x => x.EntryType == CompOffLedgerEntryType.Credit);
        Assert.Contains(ledger, x => x.EntryType == CompOffLedgerEntryType.Reserve);
        Assert.Contains(ledger, x => x.EntryType == CompOffLedgerEntryType.Consume);
        Assert.Equal(1, ledger.Count(x => x.EntryType == CompOffLedgerEntryType.Credit));
        Assert.Single(await fixture.AllocationsAsync());
    }
}
