using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit.Sdk;

namespace HRMS.Tests;

public sealed class MySqlLeaveReportsIntegrationTests
{
    [Fact]
    public async Task MySql_leave_reports_use_authoritative_rows_filters_and_safe_csv()
    {
        var connection = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) throw SkipException.ForSkip("MySQL Leave reports test not executed: HRMS_MYSQL_TEST_CONNECTION is absent.");
        var fixture = new MySqlLeaveLifecycleIntegrationTests.Fixture(connection);
        try
        {
            await fixture.SeedAsync();
            await using (var setup = fixture.CreateContext())
            {
                var employee = await setup.Employees.SingleAsync(x => x.Id == fixture.EmployeeId);
                employee.FirstName = "=Report";
                setup.Holidays.Add(new Holiday { Id = Guid.NewGuid(), TenantId = fixture.TenantId, Name = "Report Holiday", Date = new(2026, 10, 3), IsActive = true });
                setup.LeaveRequests.Add(new LeaveRequest { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, LeaveTypeId = fixture.LeaveTypeId, LeavePeriodId = fixture.LeavePeriodId, LeavePolicyVersionId = fixture.PolicyVersionId, LeavePolicyRuleId = fixture.PolicyRuleId, EmployeeEmploymentHistoryId = fixture.EmployeeHistoryId, StartDate = new(2026, 10, 1), EndDate = new(2026, 10, 2), RequestedQuantity = 2, ChargeableQuantity = 1.5m, Status = LeaveRequestStatus.Approved, SubmittedAtUtc = new DateTime(2026, 9, 28, 8, 0, 0, DateTimeKind.Utc), IdempotencyKey = "report-approved", PayloadFingerprint = "report-approved" });
                await setup.SaveChangesAsync();
            }

            await using var db = fixture.CreateContext();
            var service = new LeaveReportService(db, fixture.EmployeeTenant, new LeavePeriodResolver(db, fixture.EmployeeTenant), new FixedTimeProvider(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero)));
            var requestReport = await service.GetRequestsAsync(new() { FromDate = new(2026, 10, 1), ToDate = new(2026, 10, 2), PageSize = 10 });
            Assert.True(requestReport.Succeeded, requestReport.Message);
            Assert.Equal(1.5m, Assert.Single(requestReport.Value!.Items).Quantity);

            var usage = await service.GetUsageAsync(new() { FromDate = new(2026, 10, 1), ToDate = new(2026, 10, 2) });
            Assert.True(usage.Succeeded, usage.Message);
            Assert.Equal(1.5m, Assert.Single(usage.Value!).Quantity);

            var balances = await service.GetBalancesAsync(new() { PageSize = 10 });
            Assert.True(balances.Succeeded, balances.Message);
            Assert.Equal(10m, Assert.Single(balances.Value!.Items).Granted);

            var accounting = await service.GetAccountingAsync(new() { FromDate = new(2026, 10, 1), ToDate = new(2026, 10, 31), PageSize = 10 });
            Assert.True(accounting.Succeeded, accounting.Message);
            var pending = await service.GetPendingAsync(new() { FromDate = new(2026, 10, 1), ToDate = new(2026, 10, 31), PageSize = 10 });
            Assert.True(pending.Succeeded, pending.Message);
            Assert.Empty(pending.Value!.Items);
            var organization = await service.GetOrganizationAsync(new() { FromDate = new(2026, 10, 1), ToDate = new(2026, 10, 31) });
            Assert.True(organization.Succeeded, organization.Message);
            Assert.Equal(1, Assert.Single(organization.Value!).RequestCount);
            var workLocations = await service.GetOrganizationAsync(new() { FromDate = new(2026, 10, 1), ToDate = new(2026, 10, 31), OrganizationDimension = "workLocation" });
            Assert.True(workLocations.Succeeded, workLocations.Message);
            Assert.Equal(1, Assert.Single(workLocations.Value!).RequestCount);

            var calendar = await service.GetCalendarAsync(new() { FromDate = new(2026, 10, 1), ToDate = new(2026, 10, 5) });
            Assert.True(calendar.Succeeded, calendar.Message);
            Assert.Contains(calendar.Value!, x => x.Kind == "Holiday" && x.Name == "Report Holiday");

            var export = await service.ExportCsvAsync("requests", new() { FromDate = new(2026, 10, 1), ToDate = new(2026, 10, 2) });
            Assert.True(export.Succeeded, export.Message);
            var csv = System.Text.Encoding.UTF8.GetString(export.Value!.Content);
            Assert.Contains("Request Id", csv);
            Assert.Contains("'=Report Employee", csv);
            Assert.DoesNotContain("Reason", csv);
            var balanceExport = await service.ExportCsvAsync("balances", new());
            Assert.True(balanceExport.Succeeded, balanceExport.Message);
            Assert.True((await service.ExportCsvAsync("accounting", new() { FromDate = new(2026, 10, 1), ToDate = new(2026, 10, 31) })).Succeeded);
            Assert.True((await service.ExportCsvAsync("organization", new() { FromDate = new(2026, 10, 1), ToDate = new(2026, 10, 31), OrganizationDimension = "workLocation" })).Succeeded);

            await using var other = fixture.CreateContext(new TestTenantContext(fixture.OtherTenantId));
            var isolated = await new LeaveReportService(other, new TestTenantContext(fixture.OtherTenantId), new LeavePeriodResolver(other, new TestTenantContext(fixture.OtherTenantId)), new FixedTimeProvider(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero))).GetRequestsAsync(new() { FromDate = new(2026, 10, 1), ToDate = new(2026, 10, 2) });
            Assert.True(isolated.Succeeded, isolated.Message);
            Assert.Empty(isolated.Value!.Items);
        }
        finally { await fixture.CleanupAsync(); }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
