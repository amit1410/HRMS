using System.Net;
using System.Net.Http.Json;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Auth;
using HRMS.Domain.Authorization;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HRMS.Tests;

public sealed class AttendanceHttpManagerHarnessTests : IClassFixture<HrmsApiFactory>
{
    private readonly HrmsApiFactory _factory;

    public AttendanceHttpManagerHarnessTests(HrmsApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Employee_and_manager_accounts_resolve_distinct_linked_identities_through_http()
    {
        var builder = new AttendanceHttpEmployeeScenarioBuilder(_factory);
        var scenario = await builder.CreateManagerAsync("AM");

        var employee = await ReadMeAsync(scenario.EmployeeClient);
        var manager = await ReadMeAsync(scenario.ManagerClient);

        Assert.Equal(scenario.TenantId, employee.TenantId);
        Assert.Equal(scenario.EmployeeId, employee.EmployeeIdentity!.Employee!.Id);
        Assert.Equal(scenario.TenantId, manager.TenantId);
        Assert.Equal(scenario.ManagerEmployeeId, manager.EmployeeIdentity!.Employee!.Id);
        Assert.NotEqual(employee.EmployeeIdentity.Employee.Id, manager.EmployeeIdentity.Employee.Id);
    }

    [Fact]
    public async Task Production_manager_resolver_resolves_the_seeded_effective_relationship()
    {
        var builder = new AttendanceHttpEmployeeScenarioBuilder(_factory);
        var scenario = await builder.CreateManagerAsync("AM");
        EmployeeManagerResolution? resolution = null;

        await builder.ExecuteTenantServicesAsync(scenario, async services =>
        {
            var resolver = services.GetRequiredService<IEmployeeManagerResolver>();
            var result = await resolver.ResolveAsync(scenario.EmployeeId, scenario.RelationshipEffectiveFrom);
            Assert.True(result.Succeeded, $"Manager resolution failed: {result.Status} {result.Message}");
            resolution = result.Value;

            var db = services.GetRequiredService<HRMS.Infrastructure.Persistence.HrmsDbContext>();
            Assert.Equal(2, await db.EmployeeEmploymentHistory.CountAsync());
        });

        Assert.NotNull(resolution);
        Assert.Equal(EmployeeManagerResolutionStatus.Resolved, resolution!.Status);
        Assert.Equal(scenario.ManagerEmployeeId, resolution.ManagerId);
    }

    [Fact]
    public async Task Manager_permissions_reach_both_attendance_queue_policies_while_employee_does_not()
    {
        var builder = new AttendanceHttpEmployeeScenarioBuilder(_factory);
        var scenario = await builder.CreateManagerAsync("AM");

        var regularizations = await scenario.ManagerClient.GetAsync("/api/attendance/manager/regularizations");
        var onDuty = await scenario.ManagerClient.GetAsync("/api/attendance/manager/on-duty");
        var employeeRegularizations = await scenario.EmployeeClient.GetAsync("/api/attendance/manager/regularizations");
        var employeeOnDuty = await scenario.EmployeeClient.GetAsync("/api/attendance/manager/on-duty");

        Assert.NotEqual(HttpStatusCode.Forbidden, regularizations.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, onDuty.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, employeeRegularizations.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, employeeOnDuty.StatusCode);
    }

    [Fact]
    public async Task Two_independent_tenants_keep_employee_and_manager_graphs_separate()
    {
        var builder = new AttendanceHttpEmployeeScenarioBuilder(_factory);
        var first = await builder.CreateManagerAsync("AM");
        var second = await builder.CreateManagerAsync("AM");

        var firstEmployee = await ReadMeAsync(first.EmployeeClient);
        var firstManager = await ReadMeAsync(first.ManagerClient);
        var secondEmployee = await ReadMeAsync(second.EmployeeClient);
        var secondManager = await ReadMeAsync(second.ManagerClient);

        Assert.Equal(first.TenantId, firstEmployee.TenantId);
        Assert.Equal(first.EmployeeId, firstEmployee.EmployeeIdentity!.Employee!.Id);
        Assert.Equal(first.TenantId, firstManager.TenantId);
        Assert.Equal(first.ManagerEmployeeId, firstManager.EmployeeIdentity!.Employee!.Id);
        Assert.Equal(second.TenantId, secondEmployee.TenantId);
        Assert.Equal(second.EmployeeId, secondEmployee.EmployeeIdentity!.Employee!.Id);
        Assert.Equal(second.TenantId, secondManager.TenantId);
        Assert.Equal(second.ManagerEmployeeId, secondManager.EmployeeIdentity!.Employee!.Id);
        Assert.NotEqual(first.TenantId, second.TenantId);
    }

    private static async Task<AuthenticatedUserDto> ReadMeAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<AuthenticatedUserDto>>();
        Assert.NotNull(body?.Data);
        return body.Data!;
    }
}
