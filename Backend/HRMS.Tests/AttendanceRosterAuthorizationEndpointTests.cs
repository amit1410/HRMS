using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HRMS.Application.DTOs.Attendance;
using HRMS.Domain.Authorization;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Tests.TestSupport;

namespace HRMS.Tests;

public sealed class AttendanceRosterAuthorizationEndpointTests : IClassFixture<HrmsApiFactory>
{
    private readonly HrmsApiFactory factory;

    public AttendanceRosterAuthorizationEndpointTests(HrmsApiFactory factory) => this.factory = factory;

    [Fact]
    public async Task Employee_without_roster_permission_is_denied_for_assign_and_remove()
    {
        using var client = Client(HrmsApiFactory.Demo01Host, Guid.NewGuid(), []);
        var assign = await client.PostAsJsonAsync("/api/attendance/roster/assign", new RosterAssignmentRequest { EmployeeIds = [Guid.NewGuid()], FromDate = new(2026, 8, 15), ToDate = new(2026, 8, 15) });
        var remove = await client.DeleteAsync($"/api/attendance/roster/{Guid.NewGuid()}/2026-08-15");

        Assert.Equal(HttpStatusCode.Forbidden, assign.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, remove.StatusCode);
    }

    [Fact]
    public async Task Manager_without_roster_permission_is_denied_for_roster_mutations()
    {
        using var client = Client(HrmsApiFactory.Demo01Host, Guid.NewGuid(), [Permissions.Attendance.View]);
        var response = await client.PostAsJsonAsync("/api/attendance/roster/assign", new RosterAssignmentRequest { EmployeeIds = [Guid.NewGuid()], FromDate = new(2026, 8, 15), ToDate = new(2026, 8, 15) });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Authorized_roster_manager_reaches_the_assign_endpoint()
    {
        using var client = Client(HrmsApiFactory.Demo01Host, Guid.NewGuid(), [Permissions.Attendance.RosterManage]);
        var response = await client.PostAsJsonAsync("/api/attendance/roster/assign", new RosterAssignmentRequest { EmployeeIds = [Guid.NewGuid()], FromDate = new(2026, 8, 15), ToDate = new(2026, 8, 15) });

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Token_from_another_tenant_cannot_mutate_demo01_roster()
    {
        using var client = Client(HrmsApiFactory.Demo01Host, Guid.NewGuid(), [Permissions.Attendance.RosterManage], tenantCode: "DEMO02", tenantId: SeedData.TenantIds.Demo02);
        var response = await client.PostAsJsonAsync("/api/attendance/roster/assign", new RosterAssignmentRequest { EmployeeIds = [Guid.NewGuid()], FromDate = new(2026, 8, 15), ToDate = new(2026, 8, 15) });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Roster_query_requires_view_permission()
    {
        using var authorized = Client(HrmsApiFactory.Demo01Host, Guid.NewGuid(), [Permissions.Attendance.View]);
        using var employee = Client(HrmsApiFactory.Demo01Host, Guid.NewGuid(), []);
        var query = "/api/attendance/roster?fromDate=2026-10-01&toDate=2026-10-01";

        Assert.Equal(HttpStatusCode.OK, (await authorized.GetAsync(query)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.GetAsync(query)).StatusCode);
    }

    private HttpClient Client(string host, Guid userId, IReadOnlyList<string> permissions, string tenantCode = "DEMO01", Guid? tenantId = null)
    {
        var client = factory.CreateClientFor(host);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(userId, tenantId ?? SeedData.TenantIds.Demo01, tenantCode, $"attendance-{userId:N}@test.invalid", roles: [RoleNames.TenantAdmin], permissions: permissions));
        return client;
    }
}
