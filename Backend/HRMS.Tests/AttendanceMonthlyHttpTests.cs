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

public sealed class AttendanceMonthlyHttpTests : IClassFixture<HrmsApiFactory>
{
    private readonly HrmsApiFactory factory;

    public AttendanceMonthlyHttpTests(HrmsApiFactory factory) => this.factory = factory;

    [Fact]
    public async Task Authorized_monthly_user_can_create_process_and_read_summary_and_exceptions()
    {
        var permissions = new[]
        {
            Permissions.Attendance.MonthlyProcess,
            Permissions.Attendance.MonthlyViewAll,
            Permissions.Attendance.ExceptionView
        };
        var builder = new AttendanceHttpEmployeeScenarioBuilder(factory);
        var scenario = await builder.CreateAsync("MTH", permissions);
        await builder.ExecuteTenantAsync(scenario, async db =>
        {
            db.EmployeeEmploymentHistory.Add(new EmployeeEmploymentHistory
            {
                Id = Guid.NewGuid(), TenantId = scenario.TenantId, EmployeeId = scenario.EmployeeId,
                EffectiveFrom = new DateOnly(2026, 1, 1), EmploymentStatus = EmployeeStatus.Active,
                CreatedBy = "monthly-http-test"
            });
            await db.SaveChangesAsync();
        });

        var create = await scenario.EmployeeClient.PostAsJsonAsync("/api/attendance/periods", new { year = 2026, month = 2 });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var period = (await create.Content.ReadFromJsonAsync<ApiResponse<AttendancePeriodDto>>(jsonOptions))!.Data!;

        var process = await scenario.EmployeeClient.PostAsync($"/api/attendance/periods/{period.Id}/process", null);
        Assert.Equal(HttpStatusCode.OK, process.StatusCode);
        var overview = (await process.Content.ReadFromJsonAsync<ApiResponse<AttendancePeriodOverviewDto>>(jsonOptions))!.Data!;
        Assert.Equal(AttendancePeriodStatus.ReadyToClose, overview.Status);

        var overviewResponse = await scenario.EmployeeClient.GetAsync($"/api/attendance/periods/{period.Id}/summary");
        Assert.Equal(HttpStatusCode.OK, overviewResponse.StatusCode);

        var summaries = await scenario.EmployeeClient.GetAsync($"/api/attendance/periods/{period.Id}/summaries");
        Assert.Equal(HttpStatusCode.OK, summaries.StatusCode);
        var summaryPage = (await summaries.Content.ReadFromJsonAsync<ApiResponse<PagedResult<EmployeeAttendanceMonthlySummaryDto>>>(jsonOptions))!.Data!;
        Assert.Contains(summaryPage.Items, x => x.EmployeeId == scenario.EmployeeId && x.SourceDataVersion == period.DataVersion);

        var exceptions = await scenario.EmployeeClient.GetAsync($"/api/attendance/periods/{period.Id}/exceptions?isBlocking=true");
        Assert.Equal(HttpStatusCode.OK, exceptions.StatusCode);
        var exceptionPage = (await exceptions.Content.ReadFromJsonAsync<ApiResponse<PagedResult<AttendanceExceptionDto>>>(jsonOptions))!.Data!;
        Assert.NotEmpty(exceptionPage.Items);
    }

    [Fact]
    public async Task Monthly_process_policy_denies_an_employee_without_the_permission()
    {
        var scenario = await new AttendanceHttpEmployeeScenarioBuilder(factory).CreateAsync("MTH");

        var response = await scenario.EmployeeClient.PostAsJsonAsync("/api/attendance/periods", new { year = 2026, month = 2 });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Monthly_period_reads_are_tenant_scoped_through_http()
    {
        var permissions = new[] { Permissions.Attendance.MonthlyProcess, Permissions.Attendance.MonthlyViewAll };
        var first = await new AttendanceHttpEmployeeScenarioBuilder(factory).CreateAsync("MTH", permissions);
        var second = await new AttendanceHttpEmployeeScenarioBuilder(factory).CreateAsync("MTH", permissions);

        var create = await first.EmployeeClient.PostAsJsonAsync("/api/attendance/periods", new { year = 2026, month = 2 });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var period = (await create.Content.ReadFromJsonAsync<ApiResponse<AttendancePeriodDto>>(jsonOptions))!.Data!;

        var foreign = await second.EmployeeClient.GetAsync($"/api/attendance/periods/{period.Id}");

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
    }

    [Fact]
    public async Task Authorized_monthly_user_can_preview_close_reopen_and_read_period_events()
    {
        var permissions = new[] { Permissions.Attendance.MonthlyProcess, Permissions.Attendance.MonthlyViewAll, Permissions.Attendance.MonthlyClose, Permissions.Attendance.MonthlyReopen };
        var scenario = await new AttendanceHttpEmployeeScenarioBuilder(factory).CreateAsync("CLOSE", permissions);
        var create = await scenario.EmployeeClient.PostAsJsonAsync("/api/attendance/periods", new { year = 2026, month = 12 });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var period = (await create.Content.ReadFromJsonAsync<ApiResponse<AttendancePeriodDto>>(jsonOptions))!.Data!;
        Assert.Equal(HttpStatusCode.OK, (await scenario.EmployeeClient.PostAsync($"/api/attendance/periods/{period.Id}/process", null)).StatusCode);
        var previewResponse = await scenario.EmployeeClient.GetAsync($"/api/attendance/periods/{period.Id}/close-preview");
        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        var preview = (await previewResponse.Content.ReadFromJsonAsync<ApiResponse<AttendancePeriodClosePreviewDto>>(jsonOptions))!.Data!;
        Assert.True(preview.CanClose); Assert.True(preview.SummariesCurrent);
        var close = await scenario.EmployeeClient.PostAsJsonAsync($"/api/attendance/periods/{period.Id}/close", new { comment = "Monthly close" });
        Assert.Equal(HttpStatusCode.OK, close.StatusCode);
        var reopen = await scenario.EmployeeClient.PostAsJsonAsync($"/api/attendance/periods/{period.Id}/reopen", new { reason = "Correction review" });
        Assert.Equal(HttpStatusCode.OK, reopen.StatusCode);
        var events = await scenario.EmployeeClient.GetAsync($"/api/attendance/periods/{period.Id}/events");
        Assert.Equal(HttpStatusCode.OK, events.StatusCode);
        var history = (await events.Content.ReadFromJsonAsync<ApiResponse<IReadOnlyList<AttendancePeriodEventDto>>>(jsonOptions))!.Data!;
        Assert.Contains(history, x => x.EventType == AttendancePeriodEventType.Closed);
        Assert.Contains(history, x => x.EventType == AttendancePeriodEventType.Reopened);
    }

    [Fact]
    public async Task Monthly_close_requires_its_dedicated_permission()
    {
        var permissions = new[] { Permissions.Attendance.MonthlyProcess, Permissions.Attendance.MonthlyViewAll };
        var scenario = await new AttendanceHttpEmployeeScenarioBuilder(factory).CreateAsync("CLOSE", permissions);
        var create = await scenario.EmployeeClient.PostAsJsonAsync("/api/attendance/periods", new { year = 2026, month = 11 });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var period = (await create.Content.ReadFromJsonAsync<ApiResponse<AttendancePeriodDto>>(jsonOptions))!.Data!;
        Assert.Equal(HttpStatusCode.OK, (await scenario.EmployeeClient.PostAsync($"/api/attendance/periods/{period.Id}/process", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await scenario.EmployeeClient.PostAsync($"/api/attendance/periods/{period.Id}/close", null)).StatusCode);
    }

    private static readonly JsonSerializerOptions jsonOptions = CreateJsonOptions();

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
