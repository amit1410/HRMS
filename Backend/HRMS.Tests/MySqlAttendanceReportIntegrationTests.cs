using HRMS.Application.Services;
using HRMS.Application.Common;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit.Sdk;

namespace HRMS.Tests;

[Collection("Attendance MySQL")]
public sealed class MySqlAttendanceReportIntegrationTests
{
    [Fact]
    public async Task MySql_reports_and_exports_use_provider_backed_effective_data()
    {
        var connection = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
            throw SkipException.ForSkip("Phase 5E MySQL report test not executed: HRMS_MYSQL_TEST_CONNECTION is absent.");

        var fixture = new MySqlLeaveLifecycleIntegrationTests.Fixture(connection);
        var date = new DateOnly(2026, 10, 20);
        try
        {
            await fixture.SeedAsync();
            await using (var setup = fixture.CreateContext(fixture.EmployeeTenant))
            {
                setup.EmployeeAttendanceDays.AddRange(
                    new EmployeeAttendanceDay { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, BusinessDate = date, Status = EmployeeAttendanceDayStatus.Present, FirstPunchAtUtc = new(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc), LastPunchAtUtc = new(2026, 10, 20, 17, 0, 0, DateTimeKind.Utc), WorkedMinutes = 480, ExpectedWorkMinutes = 480, ProcessedAtUtc = DateTime.UtcNow },
                    new EmployeeAttendanceDay { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, BusinessDate = date.AddDays(1), Status = EmployeeAttendanceDayStatus.Absent, WorkedMinutes = 0, ExpectedWorkMinutes = 480, ProcessedAtUtc = DateTime.UtcNow });
                var period = new AttendancePeriod { Id = Guid.NewGuid(), TenantId = fixture.TenantId, Year = date.Year, Month = date.Month, StartDate = new(date.Year, date.Month, 1), EndDate = new(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month)), Status = AttendancePeriodStatus.Closed, DataVersion = 7 };
                setup.AttendancePeriods.Add(period);
                setup.EmployeeAttendanceMonthlySummaries.Add(new EmployeeAttendanceMonthlySummary { Id = Guid.NewGuid(), TenantId = fixture.TenantId, AttendancePeriodId = period.Id, EmployeeId = fixture.EmployeeId, EmployeeCode = "MYSQL-REPORT", EmployeeName = "MySQL Report", WorkingDays = 2, PresentDays = 1, AbsentDays = 1, ActualWorkMinutes = 480, ExpectedWorkMinutes = 960, SourceDataVersion = 7, ProcessedAtUtc = DateTime.UtcNow });
                await setup.SaveChangesAsync();

                var reports = new AttendanceReportService(setup, fixture.EmployeeTenant, new AttendanceMonthlyProcessor(setup, fixture.EmployeeTenant));
                var daily = await reports.GetDailyAsync(new() { FromDate = date, ToDate = date.AddDays(1), Page = 1, PageSize = 2 });
                Assert.True(daily.Succeeded, daily.Message);
                Assert.Equal(2, daily.Value!.TotalCount);
                Assert.Equal(2, daily.Value.Items.Count);
                Assert.Contains(daily.Value.Items, x => x.Status == EmployeeAttendanceDayStatus.Present);

                var monthly = await reports.GetMonthlyAsync(new() { PeriodId = period.Id, Page = 1, PageSize = 10 });
                Assert.True(monthly.Succeeded, monthly.Message);
                Assert.Equal(480, Assert.Single(monthly.Value!.Items).ActualWorkMinutes);

                var exceptions = await reports.GetExceptionsAsync(new() { PeriodId = period.Id, Page = 1, PageSize = 10 });
                Assert.True(exceptions.Succeeded, exceptions.Message);

                var dailyCsv = await reports.ExportDailyAsync(new() { FromDate = date, ToDate = date.AddDays(1) });
                var monthlyCsv = await reports.ExportMonthlyAsync(new() { PeriodId = period.Id });
                var exceptionCsv = await reports.ExportExceptionsAsync(new() { PeriodId = period.Id });
                Assert.True(dailyCsv.Succeeded, dailyCsv.Message);
                Assert.True(monthlyCsv.Succeeded, monthlyCsv.Message);
                Assert.True(exceptionCsv.Succeeded, exceptionCsv.Message);
                Assert.Equal(2, dailyCsv.Value!.RowCount);
                Assert.Equal(1, monthlyCsv.Value!.RowCount);
                Assert.Equal("text/csv; charset=utf-8", dailyCsv.Value.ContentType);

                await using var foreignTenant = fixture.CreateContext(new TestTenantContext(fixture.OtherTenantId));
                var foreignReports = new AttendanceReportService(foreignTenant, new TestTenantContext(fixture.OtherTenantId), new AttendanceMonthlyProcessor(foreignTenant, new TestTenantContext(fixture.OtherTenantId)));
                var foreignDaily = await foreignReports.GetDailyAsync(new() { FromDate = date, ToDate = date.AddDays(1), EmployeeId = fixture.EmployeeId });
                var foreignMonthly = await foreignReports.GetMonthlyAsync(new() { PeriodId = period.Id });
                Assert.True(foreignDaily.Succeeded, foreignDaily.Message);
                Assert.True(foreignMonthly.Succeeded, foreignMonthly.Message);
                Assert.Empty(foreignDaily.Value!.Items);
                Assert.Equal(0, foreignDaily.Value.TotalCount);
                Assert.Empty(foreignMonthly.Value!.Items);
                Assert.Equal(0, foreignMonthly.Value.TotalCount);
            }

            await using var verify = fixture.CreateContext(fixture.EmployeeTenant);
            Assert.Equal(2, await verify.EmployeeAttendanceDays.AsNoTracking().CountAsync(x => x.EmployeeId == fixture.EmployeeId && x.BusinessDate >= date && x.BusinessDate <= date.AddDays(1)));
        }
        finally
        {
            await using var cleanup = fixture.CreateContext(new TestTenantContext(fixture.TenantId));
            await cleanup.EmployeeAttendanceMonthlySummaries.ExecuteDeleteAsync();
            await cleanup.AttendancePeriods.ExecuteDeleteAsync();
            await cleanup.EmployeeAttendanceDays.ExecuteDeleteAsync();
            await fixture.CleanupAsync();
        }
    }

    [Fact]
    public async Task MySql_report_queries_and_exports_are_tenant_scoped_and_parity_preserving()
    {
        var connection = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
            throw SkipException.ForSkip("Phase 5E MySQL report isolation test not executed: HRMS_MYSQL_TEST_CONNECTION is absent.");

        var fixture = new MySqlLeaveLifecycleIntegrationTests.Fixture(connection);
        var date = new DateOnly(2026, 10, 22);
        var foreignEmployeeId = Guid.NewGuid();
        var foreignPeriodId = Guid.NewGuid();
        try
        {
            await fixture.SeedAsync();
            await using (var foreignSetup = fixture.CreateContext(new TestTenantContext(fixture.OtherTenantId)))
            {
                foreignSetup.Employees.Add(new Employee
                {
                    Id = foreignEmployeeId, TenantId = fixture.OtherTenantId, EmployeeCode = "MYSQL-B-REPORT-EMP",
                    FirstName = "MySQL Tenant B", LastName = "Report Employee", Email = $"b-{foreignEmployeeId:N}@report.test",
                    DateOfJoining = new(2026, 1, 1)
                });
                foreignSetup.EmployeeEmploymentHistory.Add(new EmployeeEmploymentHistory
                {
                    Id = Guid.NewGuid(), TenantId = fixture.OtherTenantId, EmployeeId = foreignEmployeeId,
                    EffectiveFrom = new(2026, 1, 1), EmploymentStatus = EmployeeStatus.Active, DepartmentName = "MYSQL-B-DEPARTMENT",
                    CreatedBy = "mysql-report-isolation-test"
                });
                foreignSetup.EmployeeAttendanceDays.Add(new EmployeeAttendanceDay
                {
                    Id = Guid.NewGuid(), TenantId = fixture.OtherTenantId, EmployeeId = foreignEmployeeId,
                    BusinessDate = date, Status = EmployeeAttendanceDayStatus.Incomplete, WorkedMinutes = 777,
                    ExpectedWorkMinutes = 888, HasMissingOutPunch = true, ProcessedAtUtc = DateTime.UtcNow
                });
                foreignSetup.AttendancePeriods.Add(new AttendancePeriod
                {
                    Id = foreignPeriodId, TenantId = fixture.OtherTenantId, Year = 2026, Month = 10,
                    StartDate = new(2026, 10, 1), EndDate = new(2026, 10, 31), Status = AttendancePeriodStatus.ReadyToClose, DataVersion = 11
                });
                foreignSetup.EmployeeAttendanceMonthlySummaries.Add(new EmployeeAttendanceMonthlySummary
                {
                    Id = Guid.NewGuid(), TenantId = fixture.OtherTenantId, AttendancePeriodId = foreignPeriodId,
                    EmployeeId = foreignEmployeeId, EmployeeCode = "MYSQL-B-REPORT-EMP", EmployeeName = "MySQL Tenant B Report Employee",
                    WorkingDays = 1, PresentDays = 1, ActualWorkMinutes = 777, ExpectedWorkMinutes = 888, SourceDataVersion = 11, ProcessedAtUtc = DateTime.UtcNow
                });
                await foreignSetup.SaveChangesAsync();
                var foreignReports = new AttendanceReportService(foreignSetup, new TestTenantContext(fixture.OtherTenantId), new AttendanceMonthlyProcessor(foreignSetup, new TestTenantContext(fixture.OtherTenantId)));
                var foreignDaily = await foreignReports.GetDailyAsync(new() { FromDate = date, ToDate = date, Page = 1, PageSize = 10 });
                var foreignMonthly = await foreignReports.GetMonthlyAsync(new() { PeriodId = foreignPeriodId });
                Assert.True(foreignDaily.Succeeded, foreignDaily.Message);
                Assert.True(foreignMonthly.Succeeded, foreignMonthly.Message);
                Assert.Contains(foreignDaily.Value!.Items, x => x.EmployeeId == foreignEmployeeId);
                Assert.Contains(foreignMonthly.Value!.Items, x => x.EmployeeCode == "MYSQL-B-REPORT-EMP");
                var foreignExceptions = await foreignReports.GetExceptionsAsync(new() { PeriodId = foreignPeriodId, EmployeeId = foreignEmployeeId, Page = 1, PageSize = PagedQuery.MaxPageSize });
                Assert.True(foreignExceptions.Succeeded, foreignExceptions.Message);
                Assert.NotEmpty(foreignExceptions.Value!.Items);
                var foreignDailyCsv = await foreignReports.ExportDailyAsync(new() { FromDate = date, ToDate = date });
                var foreignMonthlyCsv = await foreignReports.ExportMonthlyAsync(new() { PeriodId = foreignPeriodId });
                var foreignExceptionCsv = await foreignReports.ExportExceptionsAsync(new() { PeriodId = foreignPeriodId, EmployeeId = foreignEmployeeId });
                Assert.Contains("MYSQL-B-REPORT-EMP", System.Text.Encoding.UTF8.GetString(foreignDailyCsv.Value!.Content), StringComparison.Ordinal);
                Assert.Contains("MYSQL-B-REPORT-EMP", System.Text.Encoding.UTF8.GetString(foreignMonthlyCsv.Value!.Content), StringComparison.Ordinal);
                Assert.True(foreignExceptionCsv.Succeeded, foreignExceptionCsv.Message);
                Assert.Contains(foreignEmployeeId.ToString("D"), System.Text.Encoding.UTF8.GetString(foreignExceptionCsv.Value!.Content), StringComparison.Ordinal);
            }
            var periodId = Guid.NewGuid();
            await using (var setup = fixture.CreateContext(fixture.EmployeeTenant))
            {
                setup.EmployeeAttendanceDays.AddRange(
                    new EmployeeAttendanceDay { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, BusinessDate = date, Status = EmployeeAttendanceDayStatus.Present, FirstPunchAtUtc = new(2026, 10, 22, 9, 0, 0, DateTimeKind.Utc), LastPunchAtUtc = new(2026, 10, 22, 17, 0, 0, DateTimeKind.Utc), WorkedMinutes = 480, ExpectedWorkMinutes = 480, ProcessedAtUtc = DateTime.UtcNow },
                    new EmployeeAttendanceDay { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, BusinessDate = date.AddDays(1), Status = EmployeeAttendanceDayStatus.Incomplete, WorkedMinutes = 120, ExpectedWorkMinutes = 480, ProcessedAtUtc = DateTime.UtcNow });
                setup.AttendancePeriods.Add(new AttendancePeriod { Id = periodId, TenantId = fixture.TenantId, Year = 2026, Month = 10, StartDate = new(2026, 10, 1), EndDate = new(2026, 10, 31), Status = AttendancePeriodStatus.ReadyToClose, DataVersion = 9 });
                setup.EmployeeAttendanceMonthlySummaries.Add(new EmployeeAttendanceMonthlySummary { Id = Guid.NewGuid(), TenantId = fixture.TenantId, AttendancePeriodId = periodId, EmployeeId = fixture.EmployeeId, EmployeeCode = "MYSQL-A-EMP", EmployeeName = "MySQL Tenant A Employee", WorkingDays = 2, PresentDays = 1, IncompleteDays = 1, ActualWorkMinutes = 600, ExpectedWorkMinutes = 960, SourceDataVersion = 9, ProcessedAtUtc = DateTime.UtcNow });
                await setup.SaveChangesAsync();

                var reports = new AttendanceReportService(setup, fixture.EmployeeTenant, new AttendanceMonthlyProcessor(setup, fixture.EmployeeTenant));
                var daily = await reports.GetDailyAsync(new() { FromDate = date, ToDate = date.AddDays(1), EmployeeId = fixture.EmployeeId, Page = 1, PageSize = 1 });
                Assert.True(daily.Succeeded, daily.Message);
                Assert.Equal(2, daily.Value!.TotalCount);
                Assert.Single(daily.Value.Items);
                var dailyCsv = await reports.ExportDailyAsync(new() { FromDate = date, ToDate = date.AddDays(1), EmployeeId = fixture.EmployeeId });
                Assert.True(dailyCsv.Succeeded, dailyCsv.Message);
                Assert.Equal(2, dailyCsv.Value!.RowCount);
                var dailyText = System.Text.Encoding.UTF8.GetString(dailyCsv.Value.Content);
                Assert.Contains(fixture.EmployeeCode, dailyText, StringComparison.Ordinal);

                var monthly = await reports.GetMonthlyAsync(new() { PeriodId = periodId, EmployeeId = fixture.EmployeeId });
                Assert.True(monthly.Succeeded, monthly.Message);
                var monthlyRow = Assert.Single(monthly.Value!.Items);
                Assert.Equal(600, monthlyRow.ActualWorkMinutes);
                var monthlyCsv = await reports.ExportMonthlyAsync(new() { PeriodId = periodId, EmployeeId = fixture.EmployeeId });
                Assert.True(monthlyCsv.Succeeded, monthlyCsv.Message);
                Assert.Equal(monthly.Value.Items.Count, monthlyCsv.Value!.RowCount);
                Assert.Contains("600", System.Text.Encoding.UTF8.GetString(monthlyCsv.Value.Content), StringComparison.Ordinal);

                var exceptions = await reports.GetExceptionsAsync(new() { PeriodId = periodId, EmployeeId = fixture.EmployeeId, Page = 1, PageSize = PagedQuery.MaxPageSize });
                Assert.True(exceptions.Succeeded, exceptions.Message);
                var exceptionCsv = await reports.ExportExceptionsAsync(new() { PeriodId = periodId, EmployeeId = fixture.EmployeeId });
                Assert.True(exceptionCsv.Succeeded, exceptionCsv.Message);
                Assert.Equal(exceptions.Value!.Items.Count, exceptionCsv.Value!.RowCount);

                await using var foreignScope = fixture.CreateContext(fixture.EmployeeTenant);
                var foreignReports = new AttendanceReportService(foreignScope, fixture.EmployeeTenant, new AttendanceMonthlyProcessor(foreignScope, fixture.EmployeeTenant));
                var foreignDaily = await foreignReports.GetDailyAsync(new() { FromDate = date, ToDate = date.AddDays(1), EmployeeId = foreignEmployeeId });
                var foreignMonthly = await foreignReports.GetMonthlyAsync(new() { PeriodId = foreignPeriodId, EmployeeId = foreignEmployeeId });
                Assert.True(foreignDaily.Succeeded, foreignDaily.Message);
                Assert.True(foreignMonthly.Succeeded, foreignMonthly.Message);
                Assert.Empty(foreignDaily.Value!.Items);
                Assert.Equal(0, foreignDaily.Value.TotalCount);
                Assert.Empty(foreignMonthly.Value!.Items);
                Assert.Equal(0, foreignMonthly.Value.TotalCount);
            }
        }
        finally
        {
            await using (var foreignCleanup = fixture.CreateContext(new TestTenantContext(fixture.OtherTenantId)))
            {
                await foreignCleanup.EmployeeAttendanceMonthlySummaries.ExecuteDeleteAsync();
                await foreignCleanup.AttendancePeriods.ExecuteDeleteAsync();
                await foreignCleanup.EmployeeAttendanceDays.ExecuteDeleteAsync();
                await foreignCleanup.EmployeeEmploymentHistory.ExecuteDeleteAsync();
                await foreignCleanup.Employees.Where(x => x.Id == foreignEmployeeId).ExecuteDeleteAsync();
            }
            await using var cleanup = fixture.CreateContext(new TestTenantContext(fixture.TenantId));
            await cleanup.EmployeeAttendanceMonthlySummaries.ExecuteDeleteAsync();
            await cleanup.AttendancePeriods.ExecuteDeleteAsync();
            await cleanup.EmployeeAttendanceDays.ExecuteDeleteAsync();
            await cleanup.EmployeeEmploymentHistory.ExecuteDeleteAsync();
            await cleanup.Employees.Where(x => x.Id == foreignEmployeeId).ExecuteDeleteAsync();
            await fixture.CleanupAsync();
        }
    }
}
