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

public sealed class AttendancePhase4HttpOwnershipTenantTests : IClassFixture<HrmsApiFactory>
{
    private readonly HrmsApiFactory factory;

    public AttendancePhase4HttpOwnershipTenantTests(HrmsApiFactory factory) => this.factory = factory;

    [Fact]
    public async Task Regularization_same_tenant_foreign_employee_cannot_list_detail_or_cancel()
    {
        var builder = new AttendanceHttpEmployeeScenarioBuilder(factory);
        var managerGraph = await builder.CreateManagerAsync("OWN");
        var owner = new AttendanceHttpWorkflowScenarioBuilder(factory);
        var requestScenario = await owner.CreateAsync("OWN", createRegularization: true, createOnDuty: false);
        var secondEmployee = await builder.AddLinkedEmployeeAsync(managerGraph, "Other", [
            HRMS.Domain.Authorization.Permissions.Attendance.View,
            HRMS.Domain.Authorization.Permissions.Attendance.RegularizationRequest]);
        var foreignRequest = requestScenario.RegularizationRequestId!.Value;

        var list = await ReadAsync<PagedResult<RegularizationDto>>(
            await secondEmployee.EmployeeClient.GetAsync("/api/attendance/me/regularizations"));
        Assert.DoesNotContain(list.Items, item => item.Id == foreignRequest);
        await AssertDeniedAsync(await secondEmployee.EmployeeClient.GetAsync($"/api/attendance/me/regularizations/{foreignRequest}"));
        await AssertDeniedAsync(await secondEmployee.EmployeeClient.PostAsync(
            $"/api/attendance/me/regularizations/{foreignRequest}/cancel", null));

        await AssertRegularizationPendingAsync(requestScenario);
    }

    [Fact]
    public async Task OnDuty_same_tenant_foreign_employee_cannot_list_detail_or_cancel()
    {
        var builder = new AttendanceHttpEmployeeScenarioBuilder(factory);
        var managerGraph = await builder.CreateManagerAsync("OWN");
        var requestScenario = await new AttendanceHttpWorkflowScenarioBuilder(factory)
            .CreateAsync("OWN", createRegularization: false, createOnDuty: true);
        var secondEmployee = await builder.AddLinkedEmployeeAsync(managerGraph, "Other", [
            HRMS.Domain.Authorization.Permissions.Attendance.View,
            HRMS.Domain.Authorization.Permissions.Attendance.OnDutyRequest]);
        var foreignRequest = requestScenario.OnDutyRequestId!.Value;

        var list = await ReadAsync<PagedResult<OnDutyDto>>(
            await secondEmployee.EmployeeClient.GetAsync("/api/attendance/me/on-duty"));
        Assert.DoesNotContain(list.Items, item => item.Id == foreignRequest);
        await AssertDeniedAsync(await secondEmployee.EmployeeClient.GetAsync($"/api/attendance/me/on-duty/{foreignRequest}"));
        await AssertDeniedAsync(await secondEmployee.EmployeeClient.PostAsync(
            $"/api/attendance/me/on-duty/{foreignRequest}/cancel", null));

        await AssertOnDutyPendingAsync(requestScenario);
    }

    [Fact]
    public async Task Regularization_owner_cannot_self_approve_or_reject_through_http()
    {
        var permissions = new[]
        {
            HRMS.Domain.Authorization.Permissions.Attendance.View,
            HRMS.Domain.Authorization.Permissions.Attendance.RegularizationRequest,
            HRMS.Domain.Authorization.Permissions.Attendance.RegularizationApprove
        };
        var scenario = await new AttendanceHttpWorkflowScenarioBuilder(factory)
            .CreateAsync("SELF", createRegularization: true, createOnDuty: false, employeePermissions: permissions);
        var id = scenario.RegularizationRequestId!.Value;

        await AssertDeniedAsync(await scenario.EmployeeClient.PostAsync(
            $"/api/attendance/manager/regularizations/{id}/approve", null));
        await AssertDeniedAsync(await scenario.EmployeeClient.PostAsJsonAsync(
            $"/api/attendance/manager/regularizations/{id}/reject", new { comments = "self" }));
        await AssertRegularizationPendingAsync(scenario);
    }

    [Fact]
    public async Task OnDuty_owner_cannot_self_approve_or_reject_through_http()
    {
        var permissions = new[]
        {
            HRMS.Domain.Authorization.Permissions.Attendance.View,
            HRMS.Domain.Authorization.Permissions.Attendance.OnDutyRequest,
            HRMS.Domain.Authorization.Permissions.Attendance.OnDutyApprove
        };
        var scenario = await new AttendanceHttpWorkflowScenarioBuilder(factory)
            .CreateAsync("SELF", createRegularization: false, createOnDuty: true, employeePermissions: permissions);
        var id = scenario.OnDutyRequestId!.Value;

        await AssertDeniedAsync(await scenario.EmployeeClient.PostAsync(
            $"/api/attendance/manager/on-duty/{id}/approve", null));
        await AssertDeniedAsync(await scenario.EmployeeClient.PostAsJsonAsync(
            $"/api/attendance/manager/on-duty/{id}/reject", new { comments = "self" }));
        await AssertOnDutyPendingAsync(scenario);
    }

    [Fact]
    public async Task Regularization_cross_tenant_http_matrix_isolated_in_both_directions()
    {
        var builder = new AttendanceHttpWorkflowScenarioBuilder(factory);
        var first = await builder.CreateAsync("TEN", createRegularization: true, createOnDuty: false);
        var second = await builder.CreateAsync("TEN", createRegularization: true, createOnDuty: false);

        await AssertRegularizationForeignAccessDeniedAsync(first, second);
        await AssertRegularizationForeignAccessDeniedAsync(second, first);
    }

    [Fact]
    public async Task OnDuty_cross_tenant_http_matrix_isolated_in_both_directions()
    {
        var builder = new AttendanceHttpWorkflowScenarioBuilder(factory);
        var first = await builder.CreateAsync("TEN", createRegularization: false, createOnDuty: true);
        var second = await builder.CreateAsync("TEN", createRegularization: false, createOnDuty: true);

        await AssertOnDutyForeignAccessDeniedAsync(first, second);
        await AssertOnDutyForeignAccessDeniedAsync(second, first);
    }

    [Fact]
    public async Task Manager_employee_filter_cannot_return_foreign_tenant_rows()
    {
        var builder = new AttendanceHttpWorkflowScenarioBuilder(factory);
        var first = await builder.CreateAsync("FILTER", createRegularization: true, createOnDuty: true);
        var second = await builder.CreateAsync("FILTER", createRegularization: true, createOnDuty: true);

        var regularization = await ReadAsync<PagedResult<RegularizationDto>>(
            await first.ManagerClient.GetAsync($"/api/attendance/manager/regularizations?employeeId={second.EmployeeId}"));
        var onDuty = await ReadAsync<PagedResult<OnDutyDto>>(
            await first.ManagerClient.GetAsync($"/api/attendance/manager/on-duty?employeeId={second.EmployeeId}"));

        Assert.DoesNotContain(regularization.Items, item => item.Id == second.RegularizationRequestId);
        Assert.DoesNotContain(onDuty.Items, item => item.Id == second.OnDutyRequestId);
    }

    private async Task AssertRegularizationForeignAccessDeniedAsync(
        AttendanceHttpWorkflowScenario actor,
        AttendanceHttpWorkflowScenario target)
    {
        var regularizationId = target.RegularizationRequestId!.Value;
        var employeeList = await ReadAsync<PagedResult<RegularizationDto>>(
            await actor.EmployeeClient.GetAsync("/api/attendance/me/regularizations"));
        var managerList = await ReadAsync<PagedResult<RegularizationDto>>(
            await actor.ManagerClient.GetAsync("/api/attendance/manager/regularizations"));
        Assert.DoesNotContain(employeeList.Items, item => item.Id == regularizationId);
        Assert.DoesNotContain(managerList.Items, item => item.Id == regularizationId);
        await AssertDeniedAsync(await actor.EmployeeClient.GetAsync($"/api/attendance/me/regularizations/{regularizationId}"));
        await AssertDeniedAsync(await actor.EmployeeClient.PostAsync($"/api/attendance/me/regularizations/{regularizationId}/cancel", null));
        await AssertDeniedAsync(await actor.ManagerClient.PostAsync($"/api/attendance/manager/regularizations/{regularizationId}/approve", null));
        await AssertDeniedAsync(await actor.ManagerClient.PostAsJsonAsync(
            $"/api/attendance/manager/regularizations/{regularizationId}/reject", new { comments = "foreign" }));
        await AssertRegularizationPendingAsync(target);
    }

    private async Task AssertOnDutyForeignAccessDeniedAsync(
        AttendanceHttpWorkflowScenario actor,
        AttendanceHttpWorkflowScenario target)
    {
        var onDutyId = target.OnDutyRequestId!.Value;
        var employeeList = await ReadAsync<PagedResult<OnDutyDto>>(
            await actor.EmployeeClient.GetAsync("/api/attendance/me/on-duty"));
        var managerList = await ReadAsync<PagedResult<OnDutyDto>>(
            await actor.ManagerClient.GetAsync("/api/attendance/manager/on-duty"));
        Assert.DoesNotContain(employeeList.Items, item => item.Id == onDutyId);
        Assert.DoesNotContain(managerList.Items, item => item.Id == onDutyId);
        await AssertDeniedAsync(await actor.EmployeeClient.GetAsync($"/api/attendance/me/on-duty/{onDutyId}"));
        await AssertDeniedAsync(await actor.EmployeeClient.PostAsync($"/api/attendance/me/on-duty/{onDutyId}/cancel", null));
        await AssertDeniedAsync(await actor.ManagerClient.PostAsync($"/api/attendance/manager/on-duty/{onDutyId}/approve", null));
        await AssertDeniedAsync(await actor.ManagerClient.PostAsJsonAsync(
            $"/api/attendance/manager/on-duty/{onDutyId}/reject", new { comments = "foreign" }));
        await AssertOnDutyPendingAsync(target);
    }

    private async Task AssertRegularizationPendingAsync(AttendanceHttpWorkflowScenario scenario)
    {
        await new AttendanceHttpEmployeeScenarioBuilder(factory).ExecuteTenantAsync(scenario, async db =>
        {
            var request = await db.AttendanceRegularizationRequests.SingleAsync(x => x.Id == scenario.RegularizationRequestId);
            Assert.Equal(AttendanceRequestStatus.Pending, request.Status);
            Assert.Empty(await db.AttendanceRegularizationEvents.Where(x => x.AttendanceRegularizationRequestId == request.Id &&
                (x.EventType == AttendanceRequestEventType.Approved || x.EventType == AttendanceRequestEventType.Rejected)).ToListAsync());
            Assert.Empty(await db.AttendanceAdjustments.Where(x => x.AttendanceRegularizationRequestId == request.Id).ToListAsync());
        });
    }

    private async Task AssertOnDutyPendingAsync(AttendanceHttpWorkflowScenario scenario)
    {
        await new AttendanceHttpEmployeeScenarioBuilder(factory).ExecuteTenantAsync(scenario, async db =>
        {
            var request = await db.AttendanceOnDutyRequests.SingleAsync(x => x.Id == scenario.OnDutyRequestId);
            Assert.Equal(AttendanceRequestStatus.Pending, request.Status);
            Assert.Empty(await db.AttendanceOnDutyEvents.Where(x => x.AttendanceOnDutyRequestId == request.Id &&
                (x.EventType == AttendanceRequestEventType.Approved || x.EventType == AttendanceRequestEventType.Rejected)).ToListAsync());
            Assert.Empty(await db.EmployeeAttendanceDays.Where(x => x.EmployeeId == scenario.EmployeeId &&
                x.BusinessDate == scenario.BusinessDate && x.Status == EmployeeAttendanceDayStatus.OnDuty).ToListAsync());
        });
    }

    private static async Task AssertDeniedAsync(HttpResponseMessage response)
    {
        using (response)
        {
            Assert.True(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound,
                $"Expected 403/404, received {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        using (response)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<ApiResponse<T>>(JsonOptions);
            Assert.NotNull(body);
            Assert.NotNull(body!.Data);
            return body.Data!;
        }
    }

    private static JsonSerializerOptions JsonOptions { get; } = CreateJsonOptions();

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
