using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class AttendanceHttpWorkflowHarnessTests : IClassFixture<HrmsApiFactory>
{
    private readonly HrmsApiFactory _factory;

    public AttendanceHttpWorkflowHarnessTests(HrmsApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Pending_regularization_is_visible_to_employee_and_effective_manager()
    {
        var scenario = await new AttendanceHttpWorkflowScenarioBuilder(_factory)
            .CreateAsync("AW", createRegularization: true, createOnDuty: false);
        Assert.NotNull(scenario.RegularizationRequestId);

        var employeeList = await ReadAsync<PagedResult<RegularizationDto>>(
            await scenario.EmployeeClient.GetAsync("/api/attendance/me/regularizations"));
        var employeeDetail = await ReadAsync<RegularizationDto>(
            await scenario.EmployeeClient.GetAsync($"/api/attendance/me/regularizations/{scenario.RegularizationRequestId}"));
        var managerList = await ReadAsync<PagedResult<RegularizationDto>>(
            await scenario.ManagerClient.GetAsync("/api/attendance/manager/regularizations"));

        Assert.Contains(employeeList.Items, x => x.Id == scenario.RegularizationRequestId &&
            x.EmployeeId == scenario.EmployeeId && x.Status == AttendanceRequestStatus.Pending && x.BusinessDate == scenario.BusinessDate);
        Assert.Equal(scenario.RegularizationRequestId, employeeDetail.Id);
        Assert.Equal(AttendanceRequestStatus.Pending, employeeDetail.Status);
        Assert.Contains(managerList.Items, x => x.Id == scenario.RegularizationRequestId);

        await new AttendanceHttpEmployeeScenarioBuilder(_factory).ExecuteTenantAsync(scenario, async db =>
        {
            var request = await db.AttendanceRegularizationRequests.SingleAsync(x => x.Id == scenario.RegularizationRequestId);
            Assert.Equal(scenario.TenantId, request.TenantId);
            Assert.Equal(scenario.EmployeeId, request.EmployeeId);
            Assert.Equal(scenario.BusinessDate, request.BusinessDate);
            Assert.Equal(AttendanceRequestStatus.Pending, request.Status);
            Assert.Empty(await db.AttendanceAdjustments.Where(x => x.AttendanceRegularizationRequestId == request.Id).ToListAsync());
            Assert.DoesNotContain(await db.AttendanceRegularizationEvents.Where(x => x.AttendanceRegularizationRequestId == request.Id).ToListAsync(),
                x => x.EventType == AttendanceRequestEventType.Approved ||
                     x.EventType == AttendanceRequestEventType.Rejected);
        });
    }

    [Fact]
    public async Task Pending_on_duty_is_visible_to_employee_and_effective_manager()
    {
        var scenario = await new AttendanceHttpWorkflowScenarioBuilder(_factory)
            .CreateAsync("AW", createRegularization: false, createOnDuty: true);
        Assert.NotNull(scenario.OnDutyRequestId);

        var employeeList = await ReadAsync<PagedResult<OnDutyDto>>(
            await scenario.EmployeeClient.GetAsync("/api/attendance/me/on-duty"));
        var employeeDetail = await ReadAsync<OnDutyDto>(
            await scenario.EmployeeClient.GetAsync($"/api/attendance/me/on-duty/{scenario.OnDutyRequestId}"));
        var managerList = await ReadAsync<PagedResult<OnDutyDto>>(
            await scenario.ManagerClient.GetAsync("/api/attendance/manager/on-duty"));

        Assert.Contains(employeeList.Items, x => x.Id == scenario.OnDutyRequestId &&
            x.EmployeeId == scenario.EmployeeId && x.Status == AttendanceRequestStatus.Pending &&
            x.StartDate == scenario.BusinessDate && x.EndDate == scenario.BusinessDate);
        Assert.Equal(scenario.OnDutyRequestId, employeeDetail.Id);
        Assert.Equal(AttendanceRequestStatus.Pending, employeeDetail.Status);
        Assert.Contains(managerList.Items, x => x.Id == scenario.OnDutyRequestId);

        await new AttendanceHttpEmployeeScenarioBuilder(_factory).ExecuteTenantAsync(scenario, async db =>
        {
            var request = await db.AttendanceOnDutyRequests.SingleAsync(x => x.Id == scenario.OnDutyRequestId);
            Assert.Equal(scenario.TenantId, request.TenantId);
            Assert.Equal(scenario.EmployeeId, request.EmployeeId);
            Assert.Equal(AttendanceRequestStatus.Pending, request.Status);
            Assert.Empty(await db.AttendanceOnDutyEvents.Where(x => x.AttendanceOnDutyRequestId == request.Id &&
                (x.EventType == AttendanceRequestEventType.Approved ||
                 x.EventType == AttendanceRequestEventType.Rejected)).ToListAsync());
            Assert.Empty(await db.EmployeeAttendanceDays.Where(x => x.EmployeeId == scenario.EmployeeId &&
                x.BusinessDate == scenario.BusinessDate && x.Status == EmployeeAttendanceDayStatus.OnDuty).ToListAsync());
        });
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Workflow_builder_supports_independent_request_options(bool regularization, bool onDuty)
    {
        var scenario = await new AttendanceHttpWorkflowScenarioBuilder(_factory)
            .CreateAsync("AW", regularization, onDuty);

        Assert.Equal(regularization, scenario.RegularizationRequestId.HasValue);
        Assert.Equal(onDuty, scenario.OnDutyRequestId.HasValue);
    }

    [Fact]
    public async Task Two_independent_tenants_with_pending_workflows_see_only_their_own_positive_data()
    {
        var builder = new AttendanceHttpWorkflowScenarioBuilder(_factory);
        var first = await builder.CreateAsync("AW");
        var second = await builder.CreateAsync("AW");

        var firstRegularization = await ReadAsync<PagedResult<RegularizationDto>>(
            await first.EmployeeClient.GetAsync("/api/attendance/me/regularizations"));
        var firstOnDuty = await ReadAsync<PagedResult<OnDutyDto>>(
            await first.EmployeeClient.GetAsync("/api/attendance/me/on-duty"));
        var secondRegularization = await ReadAsync<PagedResult<RegularizationDto>>(
            await second.EmployeeClient.GetAsync("/api/attendance/me/regularizations"));
        var secondOnDuty = await ReadAsync<PagedResult<OnDutyDto>>(
            await second.EmployeeClient.GetAsync("/api/attendance/me/on-duty"));

        Assert.Contains(firstRegularization.Items, x => x.Id == first.RegularizationRequestId);
        Assert.Contains(firstOnDuty.Items, x => x.Id == first.OnDutyRequestId);
        Assert.Contains(secondRegularization.Items, x => x.Id == second.RegularizationRequestId);
        Assert.Contains(secondOnDuty.Items, x => x.Id == second.OnDutyRequestId);
        Assert.DoesNotContain(firstRegularization.Items, x => x.Id == second.RegularizationRequestId);
        Assert.DoesNotContain(firstOnDuty.Items, x => x.Id == second.OnDutyRequestId);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<T>>(JsonOptions);
        Assert.NotNull(body);
        Assert.NotNull(body.Data);
        return body.Data!;
    }

    private static JsonSerializerOptions JsonOptions { get; } = CreateJsonOptions();

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
