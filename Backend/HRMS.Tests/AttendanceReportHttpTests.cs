using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Domain.Authorization;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;

namespace HRMS.Tests;

public sealed class AttendanceReportHttpTests : IClassFixture<HrmsApiFactory>
{
    private readonly HrmsApiFactory factory;
    private readonly AttendanceHttpEmployeeScenarioBuilder builder;

    public AttendanceReportHttpTests(HrmsApiFactory factory) { this.factory = factory; builder = new AttendanceHttpEmployeeScenarioBuilder(factory); }

    [Fact]
    public async Task View_is_separate_from_export_and_reports_are_tenant_scoped()
    {
        var owner = await builder.CreateAsync("REPORT", [Permissions.Attendance.ReportView, Permissions.Attendance.ExceptionView]);
        await SeedAsync(owner);

        var daily = await owner.EmployeeClient.GetAsync("/api/attendance/reports/daily?fromDate=2026-09-10&toDate=2026-09-10");
        Assert.Equal(HttpStatusCode.OK, daily.StatusCode);
        var page = (await daily.Content.ReadFromJsonAsync<ApiResponse<PagedResult<AttendanceDailyReportRow>>>(jsonOptions))!.Data!;
        Assert.Single(page.Items);
        Assert.Equal(owner.EmployeeId, page.Items[0].EmployeeId);

        var monthly = await owner.EmployeeClient.GetAsync("/api/attendance/reports/monthly?year=2026&month=9");
        Assert.Equal(HttpStatusCode.OK, monthly.StatusCode);

        var exportDenied = await owner.EmployeeClient.GetAsync("/api/attendance/reports/daily/export?fromDate=2026-09-10&toDate=2026-09-10");
        Assert.Equal(HttpStatusCode.Forbidden, exportDenied.StatusCode);

        var exporter = await builder.CreateAsync("REPORT-EXPORT", [Permissions.Attendance.ReportView, Permissions.Attendance.ReportExport, Permissions.Attendance.ExceptionView]);
        await SeedAsync(exporter);
        var export = await exporter.EmployeeClient.GetAsync("/api/attendance/reports/daily/export?fromDate=2026-09-10&toDate=2026-09-10");
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.Equal("text/csv", export.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(export.Content.Headers.ContentDisposition?.FileName);
    }

    [Fact]
    public async Task Report_employee_filter_cannot_cross_tenant()
    {
        var tenantA = await builder.CreateAsync("REPORT-A", [Permissions.Attendance.ReportView, Permissions.Attendance.ExceptionView, Permissions.Attendance.ReportExport]);
        var tenantB = await builder.CreateAsync("REPORT-B", [Permissions.Attendance.ReportView, Permissions.Attendance.ExceptionView, Permissions.Attendance.ReportExport]);
        var tenantAPeriodId = await SeedAsync(tenantA);
        await SeedAsync(tenantB);

        var response = await tenantA.EmployeeClient.GetAsync($"/api/attendance/reports/daily?fromDate=2026-09-10&toDate=2026-09-10&employeeId={tenantB.EmployeeId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = (await response.Content.ReadFromJsonAsync<ApiResponse<PagedResult<AttendanceDailyReportRow>>>(jsonOptions))!.Data!;
        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);

        var monthly = await tenantA.EmployeeClient.GetAsync($"/api/attendance/reports/monthly?employeeId={tenantB.EmployeeId}");
        var exceptions = await tenantA.EmployeeClient.GetAsync($"/api/attendance/reports/exceptions?periodId={tenantAPeriodId}&employeeId={tenantB.EmployeeId}");
        Assert.Equal(HttpStatusCode.OK, monthly.StatusCode);
        Assert.Equal(HttpStatusCode.OK, exceptions.StatusCode);

        foreach (var path in new[]
        {
            $"/api/attendance/reports/daily/export?fromDate=2026-09-10&toDate=2026-09-10&employeeId={tenantB.EmployeeId}",
            $"/api/attendance/reports/monthly/export?employeeId={tenantB.EmployeeId}",
            $"/api/attendance/reports/exceptions/export?periodId={tenantAPeriodId}&employeeId={tenantB.EmployeeId}"
        })
        {
            var export = await tenantA.EmployeeClient.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, export.StatusCode);
            var content = await export.Content.ReadAsStringAsync();
            Assert.DoesNotContain(tenantB.EmployeeCode, content, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Employee_without_report_permission_is_denied()
    {
        var employee = await builder.CreateAsync("REPORT-EMPLOYEE");

        var daily = await employee.EmployeeClient.GetAsync("/api/attendance/reports/daily?fromDate=2026-09-10&toDate=2026-09-10");
        var monthly = await employee.EmployeeClient.GetAsync("/api/attendance/reports/monthly?year=2026&month=9");
        var export = await employee.EmployeeClient.GetAsync("/api/attendance/reports/daily/export?fromDate=2026-09-10&toDate=2026-09-10");

        Assert.Equal(HttpStatusCode.Forbidden, daily.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, monthly.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, export.StatusCode);
    }

    [Fact]
    public async Task Manager_without_report_permission_is_denied()
    {
        var manager = await builder.CreateManagerAsync(
            "REPORT-MANAGER",
            employeePermissions: [],
            managerPermissions: [Permissions.Attendance.View]);

        var response = await manager.ManagerClient.GetAsync("/api/attendance/reports/daily?fromDate=2026-09-10&toDate=2026-09-10");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Report_permissions_are_explicit_and_export_cannot_bypass_view()
    {
        var exportOnly = await builder.CreateAsync("REPORT-EXPORT-ONLY", [Permissions.Attendance.ReportExport]);
        var neither = await builder.CreateAsync("REPORT-NONE");
        var viewOnly = await builder.CreateAsync("REPORT-VIEW-ONLY", [Permissions.Attendance.ReportView]);

        var exportOnlyQuery = await exportOnly.EmployeeClient.GetAsync("/api/attendance/reports/daily?fromDate=2026-09-10&toDate=2026-09-10");
        var exportOnlyCsv = await exportOnly.EmployeeClient.GetAsync("/api/attendance/reports/daily/export?fromDate=2026-09-10&toDate=2026-09-10");
        var neitherQuery = await neither.EmployeeClient.GetAsync("/api/attendance/reports/monthly?year=2026&month=9");
        var neitherCsv = await neither.EmployeeClient.GetAsync("/api/attendance/reports/monthly/export?year=2026&month=9");
        var exceptionWithoutExceptionPermission = await viewOnly.EmployeeClient.GetAsync("/api/attendance/reports/exceptions");

        Assert.Equal(HttpStatusCode.Forbidden, exportOnlyQuery.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, exportOnlyCsv.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, neitherQuery.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, neitherCsv.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, exceptionWithoutExceptionPermission.StatusCode);
    }

    [Fact]
    public async Task Monthly_permission_matrix_is_enforced()
    {
        var viewOnly = await builder.CreateAsync("REPORT-MONTHLY-VIEW", [Permissions.Attendance.ReportView]);
        var exportOnly = await builder.CreateAsync("REPORT-MONTHLY-EXPORT", [Permissions.Attendance.ReportExport]);
        var neither = await builder.CreateAsync("REPORT-MONTHLY-NONE");
        await SeedAsync(viewOnly);
        await SeedAsync(exportOnly);
        await SeedAsync(neither);

        Assert.Equal(HttpStatusCode.OK, (await viewOnly.EmployeeClient.GetAsync("/api/attendance/reports/monthly?year=2026&month=9")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await exportOnly.EmployeeClient.GetAsync("/api/attendance/reports/monthly?year=2026&month=9")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await neither.EmployeeClient.GetAsync("/api/attendance/reports/monthly?year=2026&month=9")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewOnly.EmployeeClient.GetAsync("/api/attendance/reports/monthly/export?year=2026&month=9")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await exportOnly.EmployeeClient.GetAsync("/api/attendance/reports/monthly/export?year=2026&month=9")).StatusCode);
    }

    [Fact]
    public async Task Exception_permission_matrix_is_enforced()
    {
        var allowed = await builder.CreateAsync("REPORT-EXCEPTION-ALLOWED", [Permissions.Attendance.ReportView, Permissions.Attendance.ExceptionView]);
        var exportAllowed = await builder.CreateAsync("REPORT-EXCEPTION-EXPORT", [Permissions.Attendance.ReportView, Permissions.Attendance.ReportExport, Permissions.Attendance.ExceptionView]);
        var missingException = await builder.CreateAsync("REPORT-EXCEPTION-MISSING", [Permissions.Attendance.ReportView, Permissions.Attendance.ReportExport]);
        var exceptionOnly = await builder.CreateAsync("REPORT-EXCEPTION-ONLY", [Permissions.Attendance.ExceptionView]);
        var period = await SeedAsync(allowed);
        var exportPeriod = await SeedAsync(exportAllowed);
        var missingExceptionPeriod = await SeedAsync(missingException);
        var exceptionOnlyPeriod = await SeedAsync(exceptionOnly);

        Assert.Equal(HttpStatusCode.OK, (await allowed.EmployeeClient.GetAsync($"/api/attendance/reports/exceptions?periodId={period}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await exportAllowed.EmployeeClient.GetAsync($"/api/attendance/reports/exceptions/export?periodId={exportPeriod}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await missingException.EmployeeClient.GetAsync($"/api/attendance/reports/exceptions?periodId={missingExceptionPeriod}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await missingException.EmployeeClient.GetAsync($"/api/attendance/reports/exceptions/export?periodId={missingExceptionPeriod}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await exceptionOnly.EmployeeClient.GetAsync($"/api/attendance/reports/exceptions?periodId={exceptionOnlyPeriod}")).StatusCode);
    }

    private async Task<Guid> SeedAsync(AttendanceHttpEmployeeScenario scenario)
    {
        var periodId = Guid.NewGuid();
        await builder.ExecuteTenantAsync(scenario, async db =>
        {
        db.EmployeeAttendanceDays.Add(new EmployeeAttendanceDay
        {
            Id = Guid.NewGuid(), TenantId = scenario.TenantId, EmployeeId = scenario.EmployeeId,
            BusinessDate = new(2026, 9, 10), Status = EmployeeAttendanceDayStatus.Present,
            FirstPunchAtUtc = new(2026, 9, 10, 9, 0, 0, DateTimeKind.Utc), LastPunchAtUtc = new(2026, 9, 10, 17, 0, 0, DateTimeKind.Utc),
            WorkedMinutes = 480, ExpectedWorkMinutes = 480, ProcessedAtUtc = DateTime.UtcNow
        });
        var period = new AttendancePeriod
        {
            Id = Guid.NewGuid(), TenantId = scenario.TenantId, Year = 2026, Month = 9,
            StartDate = new(2026, 9, 1), EndDate = new(2026, 9, 30),
            Status = AttendancePeriodStatus.ReadyToClose, DataVersion = 1
        };
        period.Id = periodId;
        db.AttendancePeriods.Add(period);
        db.EmployeeAttendanceMonthlySummaries.Add(new EmployeeAttendanceMonthlySummary
        {
            Id = Guid.NewGuid(), TenantId = scenario.TenantId, AttendancePeriodId = period.Id,
            EmployeeId = scenario.EmployeeId, EmployeeCode = scenario.EmployeeCode,
            EmployeeName = "Linked Employee", WorkingDays = 1, PresentDays = 1,
            ActualWorkMinutes = 480, ExpectedWorkMinutes = 480, SourceDataVersion = 1,
            ProcessedAtUtc = DateTime.UtcNow
        });
            await db.SaveChangesAsync();
        });
        return periodId;
    }

    private static readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
}
