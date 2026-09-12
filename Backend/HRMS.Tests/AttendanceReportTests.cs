using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class AttendanceReportTests
{
    private const int ExportRowLimit = 50_000;

    [Fact]
    public async Task Daily_report_is_paged_and_uses_processed_effective_day()
    {
        using var fixture = await AttendanceTestFixture.CreateAsync();
        var userId = fixture.TenantContext.UserId!.Value;
        fixture.Context.Users.Add(new User { Id = userId, TenantId = fixture.TenantId, Email = $"{userId:N}@report.test", FirstName = "Report", LastName = "Reader" });
        var shift = await fixture.AddShiftAsync("REPORT");
        await fixture.AddRuleAsync(shift.Id, employeeId: fixture.EmployeeId);
        var date = new DateOnly(2026, 9, 10);
        fixture.Context.AttendancePunches.AddRange(
            new AttendancePunch { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, BusinessDate = date, PunchAtUtc = new(2026, 9, 10, 9, 0, 0, DateTimeKind.Utc), CapturedAtUtc = new(2026, 9, 10, 9, 0, 0, DateTimeKind.Utc), Direction = PunchDirection.In, Source = PunchSource.Biometric },
            new AttendancePunch { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, BusinessDate = date, PunchAtUtc = new(2026, 9, 10, 17, 0, 0, DateTimeKind.Utc), CapturedAtUtc = new(2026, 9, 10, 17, 0, 0, DateTimeKind.Utc), Direction = PunchDirection.Out, Source = PunchSource.Biometric });
        await fixture.Context.SaveChangesAsync();
        await using var db = fixture.CreateContext(fixture.TenantId, out _);
        var tenant = new TestTenantContext(fixture.TenantId, userId);
        var roster = new AttendanceFoundationService(db, tenant, new EffectiveEmploymentResolver(db, tenant));
        var processor = new AttendanceDayProcessor(db, tenant, roster);
        Assert.True((await processor.ProcessAsync(fixture.EmployeeId, date)).Succeeded);
        var reports = new AttendanceReportService(db, tenant, new AttendanceMonthlyProcessor(db, tenant));
        var page = await reports.GetDailyAsync(new() { FromDate = date, ToDate = date, Page = 1, PageSize = 1 });
        Assert.True(page.Succeeded, page.Message);
        var row = Assert.Single(page.Value!.Items);
        Assert.Equal(1, page.Value.TotalCount);
        Assert.Equal(date, row.BusinessDate);
        Assert.Equal(480, row.ActualWorkMinutes);

        var export = await reports.ExportDailyAsync(new() { FromDate = date, ToDate = date });
        Assert.True(export.Succeeded, export.Message);
        Assert.Equal("text/csv; charset=utf-8", export.Value!.ContentType);
        Assert.Equal(1, export.Value.RowCount);
    }

    [Fact]
    public async Task Monthly_report_reads_persisted_summary_and_is_tenant_scoped()
    {
        using var fixture = await AttendanceTestFixture.CreateAsync();
        var userId = fixture.TenantContext.UserId!.Value;
        fixture.Context.Users.Add(new User { Id = userId, TenantId = fixture.TenantId, Email = $"{userId:N}@monthly-report.test", FirstName = "Monthly", LastName = "Reader" });
        var period = new AttendancePeriod { Id = Guid.NewGuid(), TenantId = fixture.TenantId, Year = 2026, Month = 9, StartDate = new(2026, 9, 1), EndDate = new(2026, 9, 30), Status = AttendancePeriodStatus.Closed, DataVersion = 4 };
        fixture.Context.AttendancePeriods.Add(period);
        fixture.Context.EmployeeAttendanceMonthlySummaries.Add(new EmployeeAttendanceMonthlySummary { Id = Guid.NewGuid(), TenantId = fixture.TenantId, AttendancePeriodId = period.Id, EmployeeId = fixture.EmployeeId, EmployeeCode = "REPORT-1", EmployeeName = "Report Reader", WorkingDays = 1, PresentDays = 1, ActualWorkMinutes = 480, SourceDataVersion = 4, ProcessedAtUtc = DateTime.UtcNow });
        await fixture.Context.SaveChangesAsync();
        await using var db = fixture.CreateContext(fixture.TenantId, out _);
        var tenant = new TestTenantContext(fixture.TenantId, userId);
        var reports = new AttendanceReportService(db, tenant, new AttendanceMonthlyProcessor(db, tenant));
        var result = await reports.GetMonthlyAsync(new() { PeriodId = period.Id, Page = 1, PageSize = 10 });
        Assert.True(result.Succeeded, result.Message);
        var row = Assert.Single(result.Value!.Items);
        Assert.Equal(480, row.ActualWorkMinutes);
        Assert.Equal(AttendancePeriodStatus.Closed, row.PeriodStatus);
    }

    [Fact]
    public async Task Report_export_uses_csv_formula_safety_and_utf8()
    {
        var csv = new HRMS.Application.Common.CsvBuilder("Employee Code", "Employee Name");
        csv.AppendRow("+12345", "=HYPERLINK(\"https://example.test\")");
        var text = System.Text.Encoding.UTF8.GetString(csv.ToUtf8Bytes());
        Assert.StartsWith("\uFEFF", text);
        Assert.Contains("'+12345", text);
        Assert.Contains("'=HYPERLINK", text);
    }

    [Fact]
    public void Csv_export_escapes_rfc_fields_and_keeps_formula_values_recognizable()
    {
        var csv = new HRMS.Application.Common.CsvBuilder("Value");
        csv.AppendRow("Smith, John");
        csv.AppendRow("John \"Johnny\" Smith");
        csv.AppendRow("line one\nline two");
        csv.AppendRow("line one\r\nline two");
        csv.AppendRow("=2+2");
        csv.AppendRow("+12345");
        csv.AppendRow("-10+20");
        csv.AppendRow("@SUM(A1:A2)");
        csv.AppendRow("\tformula");

        var text = System.Text.Encoding.UTF8.GetString(csv.ToUtf8Bytes());
        Assert.Contains("\"Smith, John\"", text);
        Assert.Contains("\"John \"\"Johnny\"\" Smith\"", text);
        Assert.Contains("\"line one\nline two\"", text);
        Assert.Contains("\"line one\r\nline two\"", text);
        Assert.Contains("'=2+2", text);
        Assert.Contains("'+12345", text);
        Assert.Contains("'-10+20", text);
        Assert.Contains("'@SUM(A1:A2)", text);
        Assert.Contains("'\tformula", text);
    }

    [Fact]
    public async Task Attendance_report_export_at_row_limit_succeeds()
    {
        using var fixture = await AttendanceTestFixture.CreateAsync();
        var date = new DateOnly(2026, 9, 11);
        await SeedDailyRowsAsync(fixture, ExportRowLimit, date);
        await using var db = fixture.CreateContext(fixture.TenantId, out var tenant);
        var reports = new AttendanceReportService(db, tenant, new AttendanceMonthlyProcessor(db, tenant));

        var result = await reports.ExportDailyAsync(new() { FromDate = date, ToDate = date });

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(ExportRowLimit, result.Value!.RowCount);
    }

    [Fact]
    public async Task Attendance_report_export_over_row_limit_is_rejected()
    {
        using var fixture = await AttendanceTestFixture.CreateAsync();
        var date = new DateOnly(2026, 9, 12);
        await SeedDailyRowsAsync(fixture, ExportRowLimit + 1, date);
        await using var db = fixture.CreateContext(fixture.TenantId, out var tenant);
        var reports = new AttendanceReportService(db, tenant, new AttendanceMonthlyProcessor(db, tenant));

        var result = await reports.ExportDailyAsync(new() { FromDate = date, ToDate = date });

        Assert.False(result.Succeeded);
        Assert.Contains("50000", result.Message, StringComparison.Ordinal);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task Daily_report_uses_employment_effective_on_business_date_and_filters_historically()
    {
        using var fixture = await AttendanceTestFixture.CreateAsync();
        var firstDate = new DateOnly(2026, 9, 10);
        var secondDate = new DateOnly(2026, 9, 20);
        var history = await fixture.Context.EmployeeEmploymentHistory.SingleAsync(x => x.EmployeeId == fixture.EmployeeId);
        history.EffectiveTo = secondDate.AddDays(-2);
        fixture.Context.EmployeeEmploymentHistory.AddRange(
            new EmployeeEmploymentHistory { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, EffectiveFrom = new(2026, 1, 1), EffectiveTo = secondDate.AddDays(-2), DepartmentId = fixture.DepartmentId, DepartmentName = "Historical Department A", WorkLocationId = fixture.WorkLocationId, EmploymentStatus = EmployeeStatus.Active },
            new EmployeeEmploymentHistory { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, EffectiveFrom = secondDate.AddDays(-1), DepartmentId = fixture.OtherDepartmentId, DepartmentName = "Historical Department B", WorkLocationId = fixture.OtherWorkLocationId, EmploymentStatus = EmployeeStatus.Active });
        fixture.Context.EmployeeEmploymentHistory.Remove(history);
        fixture.Context.EmployeeAttendanceDays.AddRange(
            new EmployeeAttendanceDay { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, BusinessDate = firstDate, Status = EmployeeAttendanceDayStatus.Present, WorkedMinutes = 480 },
            new EmployeeAttendanceDay { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, BusinessDate = secondDate, Status = EmployeeAttendanceDayStatus.Present, WorkedMinutes = 480 });
        await fixture.Context.SaveChangesAsync();
        await using var db = fixture.CreateContext(fixture.TenantId, out var tenant);
        var reports = new AttendanceReportService(db, tenant, new AttendanceMonthlyProcessor(db, tenant));

        var all = await reports.GetDailyAsync(new() { FromDate = firstDate, ToDate = secondDate, Page = 1, PageSize = 10 });
        var departmentA = await reports.GetDailyAsync(new() { FromDate = firstDate, ToDate = secondDate, DepartmentId = fixture.DepartmentId, Page = 1, PageSize = 10 });
        var departmentB = await reports.GetDailyAsync(new() { FromDate = firstDate, ToDate = secondDate, DepartmentId = fixture.OtherDepartmentId, Page = 1, PageSize = 10 });
        var locationA = await reports.GetDailyAsync(new() { FromDate = firstDate, ToDate = secondDate, WorkLocationId = fixture.WorkLocationId, Page = 1, PageSize = 10 });
        var locationB = await reports.GetDailyAsync(new() { FromDate = firstDate, ToDate = secondDate, WorkLocationId = fixture.OtherWorkLocationId, Page = 1, PageSize = 10 });

        Assert.True(all.Succeeded, all.Message);
        Assert.Equal("Historical Department A", all.Value!.Items.Single(x => x.BusinessDate == firstDate).Department);
        Assert.Equal("Historical Department B", all.Value.Items.Single(x => x.BusinessDate == secondDate).Department);
        Assert.Equal("Noida", all.Value.Items.Single(x => x.BusinessDate == firstDate).WorkLocation);
        Assert.Equal("Gurgaon", all.Value.Items.Single(x => x.BusinessDate == secondDate).WorkLocation);
        Assert.Equal(1, departmentA.Value!.TotalCount);
        Assert.Equal(firstDate, Assert.Single(departmentA.Value.Items).BusinessDate);
        Assert.Equal(1, departmentB.Value!.TotalCount);
        Assert.Equal(secondDate, Assert.Single(departmentB.Value.Items).BusinessDate);
        Assert.Equal(firstDate, Assert.Single(locationA.Value!.Items).BusinessDate);
        Assert.Equal(secondDate, Assert.Single(locationB.Value!.Items).BusinessDate);
    }

    [Fact]
    public async Task Daily_report_reads_authoritative_effective_source_states()
    {
        using var fixture = await AttendanceTestFixture.CreateAsync();
        var dates = Enumerable.Range(1, 5).Select(day => new DateOnly(2026, 9, day)).ToArray();
        fixture.Context.EmployeeAttendanceDays.AddRange(
            new EmployeeAttendanceDay { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, BusinessDate = dates[0], Status = EmployeeAttendanceDayStatus.Present, FirstPunchAtUtc = new(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc), LastPunchAtUtc = new(2026, 9, 1, 17, 0, 0, DateTimeKind.Utc), WorkedMinutes = 480 },
            new EmployeeAttendanceDay { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, BusinessDate = dates[1], Status = EmployeeAttendanceDayStatus.Present, FirstPunchAtUtc = new(2026, 9, 2, 9, 15, 0, DateTimeKind.Utc), LastPunchAtUtc = new(2026, 9, 2, 18, 0, 0, DateTimeKind.Utc), WorkedMinutes = 525, ProcessingOutcome = "Approved Regularization" },
            new EmployeeAttendanceDay { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, BusinessDate = dates[2], Status = EmployeeAttendanceDayStatus.Present, FirstPunchAtUtc = new(2026, 9, 3, 8, 30, 0, DateTimeKind.Utc), LastPunchAtUtc = new(2026, 9, 3, 17, 30, 0, DateTimeKind.Utc), WorkedMinutes = 540, ProcessingOutcome = "Admin Correction" },
            new EmployeeAttendanceDay { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, BusinessDate = dates[3], Status = EmployeeAttendanceDayStatus.OnLeave, ProcessingOutcome = "Approved Leave" },
            new EmployeeAttendanceDay { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, BusinessDate = dates[4], Status = EmployeeAttendanceDayStatus.OnDuty, ProcessingOutcome = "Approved On Duty" });
        await fixture.Context.SaveChangesAsync();
        await using var db = fixture.CreateContext(fixture.TenantId, out var tenant);
        var reports = new AttendanceReportService(db, tenant, new AttendanceMonthlyProcessor(db, tenant));

        var result = await reports.GetDailyAsync(new() { FromDate = dates[0], ToDate = dates[^1], Page = 1, PageSize = 10 });

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(EmployeeAttendanceDayStatus.Present, result.Value!.Items.Single(x => x.BusinessDate == dates[0]).Status);
        Assert.Equal(525, result.Value.Items.Single(x => x.BusinessDate == dates[1]).ActualWorkMinutes);
        Assert.Equal(540, result.Value.Items.Single(x => x.BusinessDate == dates[2]).ActualWorkMinutes);
        Assert.Equal(EmployeeAttendanceDayStatus.OnLeave, result.Value.Items.Single(x => x.BusinessDate == dates[3]).Status);
        Assert.Equal(EmployeeAttendanceDayStatus.OnDuty, result.Value.Items.Single(x => x.BusinessDate == dates[4]).Status);
    }

    [Fact]
    public async Task Monthly_report_uses_persisted_employee_attendance_monthly_summary()
    {
        using var fixture = await AttendanceTestFixture.CreateAsync();
        var period = new AttendancePeriod { Id = Guid.NewGuid(), TenantId = fixture.TenantId, Year = 2026, Month = 9, StartDate = new(2026, 9, 1), EndDate = new(2026, 9, 30), Status = AttendancePeriodStatus.ReadyToClose, DataVersion = 12 };
        var summary = new EmployeeAttendanceMonthlySummary { Id = Guid.NewGuid(), TenantId = fixture.TenantId, AttendancePeriodId = period.Id, EmployeeId = fixture.EmployeeId, EmployeeCode = "PERSISTED", EmployeeName = "Persisted Summary", WorkingDays = 9, PresentDays = 7, AbsentDays = 1, OnLeaveDays = 1, OnDutyDays = 2, IncompleteDays = 3, NotProcessedDays = 4, ExpectedWorkMinutes = 4320, ActualWorkMinutes = 3999, ExceptionCount = 5, SourceDataVersion = 12, ProcessedAtUtc = DateTime.UtcNow };
        fixture.Context.AttendancePeriods.Add(period);
        fixture.Context.EmployeeAttendanceMonthlySummaries.Add(summary);
        await fixture.Context.SaveChangesAsync();
        await using var db = fixture.CreateContext(fixture.TenantId, out var tenant);
        var reports = new AttendanceReportService(db, tenant, new AttendanceMonthlyProcessor(db, tenant));

        var result = await reports.GetMonthlyAsync(new() { PeriodId = period.Id, Page = 1, PageSize = 10 });

        var row = Assert.Single(result.Value!.Items);
        Assert.Equal(summary.EmployeeCode, row.EmployeeCode);
        Assert.Equal(summary.WorkingDays, row.WorkingDays);
        Assert.Equal(summary.PresentDays, row.PresentDays);
        Assert.Equal(summary.AbsentDays, row.AbsentDays);
        Assert.Equal(summary.OnLeaveDays, row.OnLeaveDays);
        Assert.Equal(summary.OnDutyDays, row.OnDutyDays);
        Assert.Equal(summary.ActualWorkMinutes, row.ActualWorkMinutes);
        Assert.Equal(summary.ExpectedWorkMinutes, row.ExpectedWorkMinutes);
    }

    [Fact]
    public async Task Attendance_exception_report_reuses_phase5b_exception_semantics()
    {
        using var fixture = await AttendanceTestFixture.CreateAsync();
        var date = new DateOnly(2026, 9, 13);
        var period = new AttendancePeriod { Id = Guid.NewGuid(), TenantId = fixture.TenantId, Year = 2026, Month = 9, StartDate = new(2026, 9, 1), EndDate = new(2026, 9, 30), Status = AttendancePeriodStatus.Open, DataVersion = 1 };
        fixture.Context.AttendancePeriods.Add(period);
        fixture.Context.EmployeeAttendanceDays.Add(new EmployeeAttendanceDay { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, BusinessDate = date, Status = EmployeeAttendanceDayStatus.Incomplete, HasMissingOutPunch = true });
        await fixture.Context.SaveChangesAsync();
        await using var db = fixture.CreateContext(fixture.TenantId, out var tenant);
        var phase5b = new AttendanceMonthlyProcessor(db, tenant);
        var reports = new AttendanceReportService(db, tenant, phase5b);

        var phase5bResult = await phase5b.GetExceptionsAsync(period.Id, new() { Page = 1, PageSize = PagedQuery.MaxPageSize });
        var reportResult = await reports.GetExceptionsAsync(new() { PeriodId = period.Id, EmployeeId = fixture.EmployeeId, Page = 1, PageSize = PagedQuery.MaxPageSize });

        Assert.True(phase5bResult.Succeeded, phase5bResult.Message);
        Assert.True(reportResult.Succeeded, reportResult.Message);
        var expected = phase5bResult.Value!.Items.Where(x => x.EmployeeId == fixture.EmployeeId).Select(x => (x.BusinessDate, x.ExceptionType, x.IsBlocking)).ToHashSet();
        var actual = reportResult.Value!.Items.Select(x => (x.BusinessDate, x.ExceptionType, x.IsBlocking)).ToHashSet();
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task Daily_csv_export_formats_unicode_null_date_and_time_values_deterministically()
    {
        using var fixture = await AttendanceTestFixture.CreateAsync();
        var employee = await fixture.Context.Employees.SingleAsync(x => x.Id == fixture.EmployeeId);
        employee.FirstName = "José";
        employee.LastName = "Müller 東京";
        var date = new DateOnly(2026, 9, 14);
        fixture.Context.EmployeeAttendanceDays.Add(new EmployeeAttendanceDay { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, BusinessDate = date, Status = EmployeeAttendanceDayStatus.OnLeave, WorkedMinutes = null, ExpectedWorkMinutes = null });
        await fixture.Context.SaveChangesAsync();
        await using var db = fixture.CreateContext(fixture.TenantId, out var tenant);
        var reports = new AttendanceReportService(db, tenant, new AttendanceMonthlyProcessor(db, tenant));

        var result = await reports.ExportDailyAsync(new() { FromDate = date, ToDate = date });

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal("attendance-daily-2026-09-14-to-2026-09-14.csv", result.Value!.FileName);
        Assert.Equal("text/csv; charset=utf-8", result.Value.ContentType);
        var text = System.Text.Encoding.UTF8.GetString(result.Value.Content);
        Assert.Contains("José Müller 東京", text, StringComparison.Ordinal);
        Assert.Contains("2026-09-14", text, StringComparison.Ordinal);
        Assert.Contains(",OnLeave,", text, StringComparison.Ordinal);
    }

    private static async Task SeedDailyRowsAsync(AttendanceTestFixture fixture, int count, DateOnly date)
    {
        var employees = Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToArray();
        fixture.Context.Employees.AddRange(employees.Select((id, index) => new Employee { Id = id, TenantId = fixture.TenantId, EmployeeCode = $"CAP-{index:D5}", FirstName = "Cap", LastName = index.ToString(), Email = $"{id:N}@cap.test", DateOfJoining = new(2026, 1, 1) }));
        fixture.Context.EmployeeAttendanceDays.AddRange(employees.Select(id => new EmployeeAttendanceDay { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = id, BusinessDate = date, Status = EmployeeAttendanceDayStatus.Present, WorkedMinutes = 480, ExpectedWorkMinutes = 480 }));
        await fixture.Context.SaveChangesAsync();
    }
}
