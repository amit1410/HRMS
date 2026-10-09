using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HRMS.Domain.Authorization;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Tests.TestSupport;

namespace HRMS.Tests;

/// <summary>
/// Phase 9c-2 — every confirmation review-case endpoint is guarded by its own permission at the API level
/// (not only through frontend page access). These pin that view/manage/manager-submit/hrbp-submit are each
/// required so a future refactor cannot quietly widen access.
/// </summary>
public sealed class ConfirmationReviewCaseEndpointAuthorizationTests : IClassFixture<HrmsApiFactory>
{
    private readonly HrmsApiFactory _factory;
    public ConfirmationReviewCaseEndpointAuthorizationTests(HrmsApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Case_list_requires_view()
    {
        using var client = Client();
        var response = await client.GetAsync("/api/confirmation-review-cases");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Case_list_is_allowed_with_view()
    {
        using var client = Client(Permissions.Confirmation.ReviewCaseView);
        var response = await client.GetAsync("/api/confirmation-review-cases");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Dashboard_service_is_registered_and_authorized_request_activates_controller()
    {
        using var client = Client(Permissions.Confirmation.DashboardViewAll);
        var response = await client.GetAsync("/api/confirmation-dashboard/hr-summary");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Case_create_requires_manage_not_view()
    {
        using var client = Client(Permissions.Confirmation.ReviewCaseView);
        var response = await client.PostAsJsonAsync("/api/confirmation-review-cases", new { employeeId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Manager_submit_requires_manager_permission()
    {
        using var client = Client(Permissions.Confirmation.ReviewCaseView);
        var response = await client.PostAsJsonAsync($"/api/confirmation-review-cases/{Guid.NewGuid()}/reviews/manager/submit", new { });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Hrbp_submit_requires_hrbp_permission()
    {
        using var client = Client(Permissions.Confirmation.ReviewManagerSubmit);
        var response = await client.PostAsJsonAsync($"/api/confirmation-review-cases/{Guid.NewGuid()}/reviews/hrbp/submit", new { });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Employee_confirmation_status_requires_policy_view()
    {
        using var client = Client(Permissions.Confirmation.ReviewCaseView);
        var response = await client.GetAsync($"/api/confirmation-policies/employees/{Guid.NewGuid()}/status");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Employee_confirmation_status_is_reachable_with_policy_view()
    {
        using var client = Client(Permissions.Confirmation.PolicyView);
        var response = await client.GetAsync($"/api/confirmation-policies/employees/{Guid.NewGuid()}/status");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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
