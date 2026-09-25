using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HRMS.Domain.Authorization;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Tests.TestSupport;

namespace HRMS.Tests;

public sealed class CompOffSecurityTests : IClassFixture<HrmsApiFactory>
{
    private readonly HrmsApiFactory factory;

    public CompOffSecurityTests(HrmsApiFactory factory) => this.factory = factory;

    [Fact]
    public async Task Employee_can_view_own_comp_off()
    {
        using var client = Client([Permissions.Attendance.CompOffViewSelf]);
        var response = await client.GetAsync("/api/attendance/comp-off/balance");
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Employee_cannot_view_other_employee_comp_off()
    {
        using var client = Client([Permissions.Attendance.CompOffViewSelf]);
        var response = await client.PostAsJsonAsync($"/api/attendance/comp-off/leave/{Guid.NewGuid()}/reserve", new { employeeId = Guid.NewGuid(), minutes = 1 });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Employee_cannot_self_approve_credit()
    {
        using var client = Client([Permissions.Attendance.CompOffViewSelf]);
        var response = await client.PostAsync($"/api/attendance/comp-off/earnings/{Guid.NewGuid()}/approve", null);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Manager_team_scope_enforced()
    {
        using var client = Client([Permissions.Attendance.CompOffApprove]);
        var response = await client.PostAsync($"/api/attendance/comp-off/earnings/{Guid.NewGuid()}/approve", null);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task HRBP_scope_enforced()
    {
        using var client = Client([Permissions.Attendance.CompOffViewSelf, Permissions.Attendance.CompOffViewAll]);
        var response = await client.GetAsync("/api/attendance/comp-off/earnings");
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task TimeManager_scope_enforced()
    {
        using var client = Client([Permissions.Attendance.CompOffManage]);
        var response = await client.PostAsync($"/api/attendance/comp-off/expiry?asOfDate=2026-09-16", null);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Cross_tenant_access_denied()
    {
        using var client = Client([Permissions.Attendance.CompOffViewAll], tenantCode: "DEMO02", tenantId: SeedData.TenantIds.Demo02, hostCode: "DEMO01");
        var response = await client.GetAsync("/api/attendance/comp-off/balance");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Employee_cannot_directly_adjust_ledger()
    {
        using var client = Client([Permissions.Attendance.CompOffViewSelf]);
        var response = await client.PostAsync($"/api/attendance/comp-off/leave/{Guid.NewGuid()}/restore", null);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Unauthorized_user_cannot_reverse_credit()
    {
        using var client = Client([Permissions.Attendance.CompOffViewSelf]);
        var response = await client.PostAsync($"/api/attendance/comp-off/leave/{Guid.NewGuid()}/release", null);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Leave_consumption_cannot_use_other_employee_credit()
    {
        using var client = Client([Permissions.Attendance.CompOffViewSelf]);
        var response = await client.PostAsJsonAsync($"/api/attendance/comp-off/leave/{Guid.NewGuid()}/reserve", new { employeeId = Guid.NewGuid(), minutes = 240 });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private HttpClient Client(IReadOnlyList<string> permissions, string tenantCode = "DEMO01", Guid? tenantId = null, string? hostCode = null)
    {
        var userId = Guid.NewGuid();
        var client = factory.CreateClientFor((hostCode ?? tenantCode) == "DEMO02" ? HrmsApiFactory.Demo02Host : HrmsApiFactory.Demo01Host);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(userId, tenantId ?? SeedData.TenantIds.Demo01, tenantCode, $"comp-off-security-{userId:N}@test.invalid", permissions: permissions));
        return client;
    }
}
