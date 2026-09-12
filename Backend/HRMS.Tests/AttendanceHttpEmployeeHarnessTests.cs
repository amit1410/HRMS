using System.Net;
using System.Net.Http.Json;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Auth;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class AttendanceHttpEmployeeHarnessTests : IClassFixture<HrmsApiFactory>
{
    private readonly HrmsApiFactory _factory;

    public AttendanceHttpEmployeeHarnessTests(HrmsApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Tenant_routing_and_linked_employee_identity_are_resolved_through_http()
    {
        var scenario = await new AttendanceHttpEmployeeScenarioBuilder(_factory).CreateAsync("AE");
        using var client = scenario.EmployeeClient;

        var response = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<AuthenticatedUserDto>>();

        Assert.NotNull(body?.Data);
        Assert.Equal(scenario.TenantId, body.Data!.TenantId);
        Assert.Equal(scenario.TenantCode, body.Data.TenantCode);
        Assert.Equal(scenario.UserId, body.Data.Id);
        Assert.NotNull(body.Data.EmployeeIdentity?.Employee);
        Assert.Equal(scenario.EmployeeId, body.Data.EmployeeIdentity!.Employee!.Id);
        Assert.NotEqual(scenario.UserId, body.Data.EmployeeIdentity.Employee.Id);
    }

    [Fact]
    public async Task Fresh_tenant_context_verifies_the_authoritative_account_employee_link()
    {
        var scenario = await new AttendanceHttpEmployeeScenarioBuilder(_factory).CreateAsync("AE");
        var builder = new AttendanceHttpEmployeeScenarioBuilder(_factory);

        await builder.ExecuteTenantAsync(scenario, async db =>
        {
            var link = await db.AccountEmployeeCurrentLinks
                .SingleOrDefaultAsync(x => x.TenantId == scenario.TenantId && x.UserId == scenario.UserId);
            Assert.NotNull(link);
            Assert.Equal(scenario.TenantId, link!.TenantId);
            Assert.Equal(scenario.UserId, link.UserId);
            Assert.Equal(scenario.EmployeeId, link.EmployeeId);
        });
    }

    [Fact]
    public async Task An_unlinked_authenticated_user_cannot_resolve_employee_identity()
    {
        var builder = new AttendanceHttpEmployeeScenarioBuilder(_factory);
        var scenario = await builder.CreateAsync("AE");
        var unlinked = await builder.CreateUnlinkedUserAsync(scenario);
        using var client = unlinked.Client;

        var response = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<AuthenticatedUserDto>>();
        Assert.True(body?.Success);
        Assert.NotNull(body?.Data);
        Assert.NotNull(body.Data!.EmployeeIdentity);
        Assert.Equal("Unlinked", body.Data.EmployeeIdentity!.Status);
        Assert.Null(body.Data.EmployeeIdentity.Employee);
    }

    [Fact]
    public async Task Two_independent_tenant_graphs_resolve_only_their_own_linked_identity()
    {
        var builder = new AttendanceHttpEmployeeScenarioBuilder(_factory);
        var first = await builder.CreateAsync("AE");
        var second = await builder.CreateAsync("AE");

        var firstResponse = await first.EmployeeClient.GetAsync("/api/auth/me");
        var secondResponse = await second.EmployeeClient.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);

        var firstBody = await firstResponse.Content.ReadFromJsonAsync<ApiResponse<AuthenticatedUserDto>>();
        var secondBody = await secondResponse.Content.ReadFromJsonAsync<ApiResponse<AuthenticatedUserDto>>();
        Assert.Equal(first.EmployeeId, firstBody!.Data!.EmployeeIdentity!.Employee!.Id);
        Assert.Equal(second.EmployeeId, secondBody!.Data!.EmployeeIdentity!.Employee!.Id);
        Assert.NotEqual(first.TenantId, second.TenantId);
        Assert.NotEqual(first.EmployeeId, second.EmployeeId);

        first.EmployeeClient.Dispose();
        second.EmployeeClient.Dispose();
    }
}
