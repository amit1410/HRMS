using System.Net;
using System.Net.Http.Json;
using Xunit.Sdk;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HRMS.Tests;

[Collection("Attendance MySQL")]
public sealed class MySqlAttendancePhase4HttpAuthorizationTests
{
    [Fact]
    public async Task MySql_cross_tenant_regularization_approval_is_denied_both_directions()
    {
        await WithFactory(async (factory, builder) =>
        {
            var first = await builder.CreateAsync("MYAUTH", true, false);
            var second = await builder.CreateAsync("MYAUTH", true, false);
            await AssertDeniedAsync(await first.ManagerClient.PostAsync(
                $"/api/attendance/manager/regularizations/{second.RegularizationRequestId}/approve", null));
            await AssertDeniedAsync(await second.ManagerClient.PostAsync(
                $"/api/attendance/manager/regularizations/{first.RegularizationRequestId}/approve", null));
            await AssertRegularizationPendingAsync(factory, first);
            await AssertRegularizationPendingAsync(factory, second);
        });
    }

    [Fact]
    public async Task MySql_cross_tenant_on_duty_approval_is_denied_both_directions()
    {
        await WithFactory(async (factory, builder) =>
        {
            var first = await builder.CreateAsync("MYAUTH", false, true);
            var second = await builder.CreateAsync("MYAUTH", false, true);
            await AssertDeniedAsync(await first.ManagerClient.PostAsync(
                $"/api/attendance/manager/on-duty/{second.OnDutyRequestId}/approve", null));
            await AssertDeniedAsync(await second.ManagerClient.PostAsync(
                $"/api/attendance/manager/on-duty/{first.OnDutyRequestId}/approve", null));
            await AssertOnDutyPendingAsync(factory, first);
            await AssertOnDutyPendingAsync(factory, second);
        });
    }

    [Fact]
    public async Task MySql_same_tenant_foreign_employee_cannot_cancel_requests()
    {
        await WithFactory(async (factory, builder) =>
        {
            var graph = await new AttendanceHttpEmployeeScenarioBuilder(factory).CreateManagerAsync("MYAUTH", employeePermissions: [
                HRMS.Domain.Authorization.Permissions.Attendance.View,
                HRMS.Domain.Authorization.Permissions.Attendance.RegularizationRequest,
                HRMS.Domain.Authorization.Permissions.Attendance.OnDutyRequest]);
            var other = await new AttendanceHttpEmployeeScenarioBuilder(factory).AddLinkedEmployeeAsync(graph, "Other", [
                HRMS.Domain.Authorization.Permissions.Attendance.View,
                HRMS.Domain.Authorization.Permissions.Attendance.RegularizationRequest,
                HRMS.Domain.Authorization.Permissions.Attendance.OnDutyRequest]);
            var regularizationId = await builder.CreatePendingRegularizationAsync(graph, graph.EmployeeClient, graph.RelationshipEffectiveFrom);
            var onDutyId = await builder.CreatePendingOnDutyAsync(graph.EmployeeClient, graph.RelationshipEffectiveFrom.AddDays(1));
            await AssertDeniedAsync(await other.EmployeeClient.PostAsync(
                $"/api/attendance/me/regularizations/{regularizationId}/cancel", null));
            await AssertDeniedAsync(await other.EmployeeClient.PostAsync(
                $"/api/attendance/me/on-duty/{onDutyId}/cancel", null));
        });
    }

    [Fact]
    public async Task MySql_authorized_manager_can_approve_own_tenant_requests()
    {
        await WithFactory(async (factory, builder) =>
        {
            var regularization = await builder.CreateAsync("MYAUTH", true, false);
            var onDuty = await builder.CreateAsync("MYAUTH", false, true);
            var regularizationResponse = await regularization.ManagerClient.PostAsync(
                $"/api/attendance/manager/regularizations/{regularization.RegularizationRequestId}/approve", null);
            var onDutyResponse = await onDuty.ManagerClient.PostAsync(
                $"/api/attendance/manager/on-duty/{onDuty.OnDutyRequestId}/approve", null);
            Assert.Equal(HttpStatusCode.OK, regularizationResponse.StatusCode);
            Assert.Equal(HttpStatusCode.OK, onDutyResponse.StatusCode);
        });
    }

    private static async Task WithFactory(
        Func<MySqlApiFactory, AttendanceHttpWorkflowScenarioBuilder, Task> action)
    {
        var tenantConnection = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        var catalogConnection = Environment.GetEnvironmentVariable("HRMS_MYSQL_CATALOG_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(tenantConnection) || string.IsNullOrWhiteSpace(catalogConnection))
            throw SkipException.ForSkip("MySQL HTTP authorization tests require both connection strings.");

        using var factory = new MySqlApiFactory(tenantConnection, catalogConnection);
        await action(factory, new AttendanceHttpWorkflowScenarioBuilder(factory));
    }

    private static async Task AssertRegularizationPendingAsync(
        MySqlApiFactory factory,
        AttendanceHttpWorkflowScenario scenario)
    {
        await factory.ExecuteInTenantScopeAsync(scenario.Host, async services =>
        {
            var db = services.GetRequiredService<HRMS.Infrastructure.Persistence.HrmsDbContext>();
            var request = await db.AttendanceRegularizationRequests.SingleAsync(x => x.Id == scenario.RegularizationRequestId);
            Assert.Equal(AttendanceRequestStatus.Pending, request.Status);
            Assert.Empty(await db.AttendanceAdjustments.Where(x => x.AttendanceRegularizationRequestId == request.Id).ToListAsync());
        });
    }

    private static async Task AssertOnDutyPendingAsync(
        MySqlApiFactory factory,
        AttendanceHttpWorkflowScenario scenario)
    {
        await factory.ExecuteInTenantScopeAsync(scenario.Host, async services =>
        {
            var db = services.GetRequiredService<HRMS.Infrastructure.Persistence.HrmsDbContext>();
            var request = await db.AttendanceOnDutyRequests.SingleAsync(x => x.Id == scenario.OnDutyRequestId);
            Assert.Equal(AttendanceRequestStatus.Pending, request.Status);
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
}
