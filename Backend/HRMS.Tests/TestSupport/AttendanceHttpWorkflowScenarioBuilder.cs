using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Domain.Entities;
using HRMS.Domain.Authorization;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests.TestSupport;

public sealed class AttendanceHttpWorkflowScenarioBuilder(HrmsApiFactory factory)
{
    public async Task<Guid> CreatePendingRegularizationAsync(
        AttendanceHttpManagerScenario graph,
        HttpClient employeeClient,
        DateOnly businessDate)
    {
        await SeedEligibleDayAsync(graph, businessDate);
        return await SubmitRegularizationAsync(employeeClient, businessDate);
    }

    public Task<Guid> CreatePendingOnDutyAsync(HttpClient employeeClient, DateOnly businessDate) =>
        SubmitOnDutyAsync(employeeClient, businessDate);

    public async Task<AttendanceHttpWorkflowScenario> CreateAsync(
        string? prefix = null,
        bool createRegularization = true,
        bool createOnDuty = true,
        IEnumerable<string>? employeePermissions = null)
    {
        var graph = await new AttendanceHttpEmployeeScenarioBuilder(factory).CreateManagerAsync(
            prefix,
            employeePermissions: employeePermissions ?? [
                    Permissions.Attendance.View,
                    Permissions.Attendance.RegularizationRequest,
                    Permissions.Attendance.OnDutyRequest]);
        var businessDate = graph.RelationshipEffectiveFrom;
        Guid? regularizationId = null;
        Guid? onDutyId = null;

        if (createRegularization)
        {
            await SeedEligibleDayAsync(graph, businessDate);
            regularizationId = await SubmitRegularizationAsync(graph.EmployeeClient, businessDate);
        }

        if (createOnDuty)
            onDutyId = await SubmitOnDutyAsync(graph.EmployeeClient, businessDate);

        return new AttendanceHttpWorkflowScenario(
            graph.TenantId, graph.Host, graph.EmployeeId, graph.ManagerEmployeeId,
            graph.EmployeeClient, graph.ManagerClient, businessDate, regularizationId, onDutyId);
    }

    private static async Task<Guid> SubmitRegularizationAsync(
        HttpClient employeeClient,
        DateOnly businessDate)
    {
        using var response = await employeeClient.PostAsJsonAsync(
            "/api/attendance/me/regularizations",
            new
            {
                businessDate,
                requestType = AttendanceRegularizationType.MissingOutPunch,
                proposedInAtUtc = (DateTime?)null,
                proposedOutAtUtc = businessDate.ToDateTime(new TimeOnly(18, 0), DateTimeKind.Utc),
                reason = "Test missing out punch"
            });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<RegularizationDto>>(JsonOptions);
        Assert.NotNull(body?.Data);
        Assert.Equal(AttendanceRequestStatus.Pending, body.Data!.Status);
        return body.Data.Id;
    }

    private static async Task<Guid> SubmitOnDutyAsync(
        HttpClient employeeClient,
        DateOnly businessDate)
    {
        using var response = await employeeClient.PostAsJsonAsync(
            "/api/attendance/me/on-duty",
            new { startDate = businessDate, endDate = businessDate, reason = "Test client visit" });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<OnDutyDto>>(JsonOptions);
        Assert.NotNull(body?.Data);
        Assert.Equal(AttendanceRequestStatus.Pending, body.Data!.Status);
        return body.Data.Id;
    }

    private async Task SeedEligibleDayAsync(AttendanceHttpManagerScenario graph, DateOnly date)
    {
        await new AttendanceHttpEmployeeScenarioBuilder(factory).ExecuteTenantAsync(graph, async db =>
        {
            db.EmployeeAttendanceDays.Add(new EmployeeAttendanceDay
            {
                Id = Guid.NewGuid(), TenantId = graph.TenantId, EmployeeId = graph.EmployeeId,
                BusinessDate = date, Status = EmployeeAttendanceDayStatus.Incomplete,
                RosterAssignmentSource = RosterAssignmentSource.System,
                RosterDayType = RosterDayType.Shift, HasMissingOutPunch = true
            });
            await db.SaveChangesAsync();
        });
    }

    private static JsonSerializerOptions JsonOptions { get; } = CreateJsonOptions();

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}

public sealed record AttendanceHttpWorkflowScenario(
    Guid TenantId,
    string Host,
    Guid EmployeeId,
    Guid ManagerEmployeeId,
    HttpClient EmployeeClient,
    HttpClient ManagerClient,
    DateOnly BusinessDate,
    Guid? RegularizationRequestId,
    Guid? OnDutyRequestId);
