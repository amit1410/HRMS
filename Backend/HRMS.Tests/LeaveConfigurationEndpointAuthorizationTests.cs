using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HRMS.Domain.Authorization;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Tests.TestSupport;

namespace HRMS.Tests;

public sealed class LeaveConfigurationEndpointAuthorizationTests : IClassFixture<HrmsApiFactory>
{
    private readonly HrmsApiFactory _factory;

    public LeaveConfigurationEndpointAuthorizationTests(HrmsApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Policy_view_is_required_for_configuration_reads()
    {
        using var client = Client();

        var response = await client.GetAsync("/api/leave-policies");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Policy_manage_does_not_grant_publish_or_retire()
    {
        using var client = Client(Permissions.Leave.PolicyManage);
        var id = Guid.NewGuid();

        var publish = await client.PostAsync($"/api/leave-policies/{id}/versions/{id}/publish", null);
        var retire = await client.PostAsync($"/api/leave-policies/{id}/versions/{id}/retire", null);

        Assert.Equal(HttpStatusCode.Forbidden, publish.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, retire.StatusCode);
    }

    [Fact]
    public async Task Policy_publish_is_separate_from_policy_manage_for_mutations()
    {
        using var client = Client(Permissions.Leave.PolicyPublish);

        var response = await client.PostAsJsonAsync(
            "/api/leave-policies",
            new { code = "INVALID", name = "Not permitted" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Eligibility_read_requires_policy_view()
    {
        using var client = Client();
        var id = Guid.NewGuid();

        var response = await client.GetAsync($"/api/leave-policies/{id}/versions/{id}/leave-types/{id}/eligibility");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Eligibility_write_requires_policy_manage()
    {
        using var client = Client(Permissions.Leave.PolicyView);
        var id = Guid.NewGuid();

        var response = await client.PutAsJsonAsync(
            $"/api/leave-policies/{id}/versions/{id}/leave-types/{id}/eligibility",
            new { eligibilityMode = "Immediate", probationMode = "Allowed", noticePeriodMode = "Allowed" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Entitlement_read_requires_policy_view()
    {
        using var client = Client();
        var id = Guid.NewGuid();

        var response = await client.GetAsync($"/api/leave-policies/{id}/versions/{id}/leave-types/{id}/entitlement");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Entitlement_write_requires_policy_manage()
    {
        using var client = Client(Permissions.Leave.PolicyView);
        var id = Guid.NewGuid();

        var response = await client.PutAsJsonAsync(
            $"/api/leave-policies/{id}/versions/{id}/leave-types/{id}/entitlement",
            new { entitlementMode = "Allocated", entitlementSource = "PolicyAccrual", entitlementQuantity = 12, accrualFrequency = "Annual" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Cancellation_read_requires_policy_view()
    {
        using var client = Client();
        var id = Guid.NewGuid();

        var response = await client.GetAsync($"/api/leave-policies/{id}/versions/{id}/leave-types/{id}/cancellation");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Cancellation_write_requires_policy_manage()
    {
        using var client = Client(Permissions.Leave.PolicyView);
        var id = Guid.NewGuid();

        var response = await client.PutAsJsonAsync(
            $"/api/leave-policies/{id}/versions/{id}/leave-types/{id}/cancellation",
            new { withdrawAllowed = true, cancelAllowed = false, modifyAllowed = false });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private HttpClient Client(params string[] permissions)
    {
        var client = _factory.CreateClientFor(HrmsApiFactory.Demo01Host);
        var token = TestTokens.Create(
            SeedData.Users[0].Id,
            SeedData.TenantIds.Demo01,
            "DEMO01",
            "admin@demo01.com",
            roles: [RoleNames.TenantAdmin],
            permissions: permissions);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
