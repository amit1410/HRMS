using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HRMS.Domain.Authorization;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Tests.TestSupport;

namespace HRMS.Tests;

public sealed class AttendanceWorkflowAuthorizationHttpTests : IClassFixture<HrmsApiFactory>
{
    private readonly HrmsApiFactory factory;

    public AttendanceWorkflowAuthorizationHttpTests(HrmsApiFactory factory) => this.factory = factory;

    [Fact]
    public async Task Regularization_manager_endpoints_require_permission_even_when_relationship_exists()
    {
        using var client = Client([]);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/attendance/manager/regularizations")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"/api/attendance/manager/regularizations/{Guid.NewGuid()}/approve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/attendance/manager/regularizations/{Guid.NewGuid()}/reject", new { comments = "no" })).StatusCode);
    }

    [Fact]
    public async Task On_duty_manager_endpoints_require_permission_even_when_relationship_exists()
    {
        using var client = Client([]);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/attendance/manager/on-duty")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"/api/attendance/manager/on-duty/{Guid.NewGuid()}/approve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/attendance/manager/on-duty/{Guid.NewGuid()}/reject", new { comments = "no" })).StatusCode);
    }

    private HttpClient Client(IReadOnlyList<string> permissions)
    {
        var client = factory.CreateClientFor(HrmsApiFactory.Demo01Host);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(Guid.NewGuid(), SeedData.TenantIds.Demo01, "DEMO01", permissions: permissions));
        return client;
    }
}
