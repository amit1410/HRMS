using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HRMS.Application.Abstractions;
using HRMS.API.Controllers;
using HRMS.API.Security;
using HRMS.Domain.Authorization;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Tests.TestSupport;

namespace HRMS.Tests;

public sealed class OvertimeSecurityTests : IClassFixture<HrmsApiFactory>
{
    private readonly HrmsApiFactory factory;
    public OvertimeSecurityTests(HrmsApiFactory factory) => this.factory = factory;

    [Fact]
    public async Task Employee_can_create_own_ot_request()
    {
        using var client = Client([Permissions.Attendance.OvertimeRequest]);
        var response = await client.PostAsJsonAsync("/api/attendance/overtime/requests", new { employeeId = Guid.NewGuid(), workDate = "2026-09-15", requestedMinutes = 60, category = 0, reason = "self" });
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Employee_cannot_create_ot_for_other_employee()
    {
        using var client = Client([]);
        var response = await client.PostAsJsonAsync("/api/attendance/overtime/requests", new { employeeId = Guid.NewGuid(), workDate = "2026-09-15", requestedMinutes = 60, category = 0 });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Employee_cannot_self_approve()
    {
        using var client = Client([]);
        var response = await client.PostAsJsonAsync($"/api/attendance/overtime/requests/{Guid.NewGuid()}/approve", new { });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Manager_team_scope_enforced()
    {
        using var client = Client([Permissions.Attendance.OvertimeApprove]);
        var response = await client.PostAsJsonAsync($"/api/attendance/overtime/requests/{Guid.NewGuid()}/approve", new { });
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task HRBP_scope_enforced()
    {
        using var client = Client([Permissions.Attendance.OvertimeViewAll]);
        var response = await client.GetAsync($"/api/attendance/overtime/snapshot?employeeId={Guid.NewGuid()}&periodStart=2026-09-01&periodEnd=2026-09-30");
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task TimeManager_authorized_operations()
    {
        using var client = Client([Permissions.Attendance.OvertimeFinalize]);
        var response = await client.PostAsync($"/api/attendance/overtime/periods/{Guid.NewGuid()}/finalize", null);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Cross_tenant_ot_access_denied()
    {
        using var client = Client([Permissions.Attendance.OvertimeViewAll], tenantCode: "DEMO02", tenantId: SeedData.TenantIds.Demo02, hostCode: "DEMO01");
        var response = await client.GetAsync($"/api/attendance/overtime/snapshot?employeeId={Guid.NewGuid()}&periodStart=2026-09-01&periodEnd=2026-09-30");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Payroll_reader_cannot_modify_ot()
    {
        using var client = Client([Permissions.Payroll.RunViewResults]);
        var response = await client.PostAsJsonAsync($"/api/attendance/overtime/requests/{Guid.NewGuid()}/approve", new { });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Unauthorized_user_cannot_reopen_finalized_ot()
    {
        using var client = Client([]);
        var response = await client.PostAsJsonAsync($"/api/attendance/overtime/periods/{Guid.NewGuid()}/reopen", new { });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Employee_cannot_modify_approved_minutes()
    {
        Assert.Null(typeof(OvertimeRequestRequest).GetProperty("ActualEligibleMinutes"));
        Assert.Null(typeof(OvertimeRequestRequest).GetProperty("ApprovedMinutes"));
        using var client = Client([Permissions.Attendance.OvertimeRequest]);
        var response = await client.PostAsJsonAsync($"/api/attendance/overtime/requests/{Guid.NewGuid()}/approve", new { approvedMinutes = 999 });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private HttpClient Client(IReadOnlyList<string> permissions, string tenantCode = "DEMO01", Guid? tenantId = null, string? hostCode = null)
    {
        var userId = Guid.NewGuid();
        var client = factory.CreateClientFor((hostCode ?? tenantCode) == "DEMO02" ? HrmsApiFactory.Demo02Host : HrmsApiFactory.Demo01Host);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(userId, tenantId ?? SeedData.TenantIds.Demo01, tenantCode, $"ot-security-{userId:N}@test.invalid", permissions: permissions));
        return client;
    }
}
