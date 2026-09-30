using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HRMS.Domain.Authorization;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Tests.TestSupport;

namespace HRMS.Tests;

/// <summary>
/// Every Bank master endpoint is guarded by its own permission. These tests pin that a lesser permission
/// (or none) is refused, and that the matching permission is accepted, so a future refactor cannot quietly
/// widen access.
/// </summary>
public sealed class BankMasterEndpointAuthorizationTests : IClassFixture<HrmsApiFactory>
{
    private readonly HrmsApiFactory _factory;

    public BankMasterEndpointAuthorizationTests(HrmsApiFactory factory) => _factory = factory;

    [Fact]
    public async Task List_requires_bank_master_view()
    {
        using var client = Client();

        var response = await client.GetAsync("/api/banks");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task List_is_allowed_with_view()
    {
        using var client = Client(Permissions.BankMaster.View);

        var response = await client.GetAsync("/api/banks");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Create_requires_create_not_view()
    {
        using var client = Client(Permissions.BankMaster.View);

        var response = await client.PostAsJsonAsync("/api/banks", new { code = "NEW", name = "New Bank", isActive = true });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Update_requires_edit_not_view()
    {
        using var client = Client(Permissions.BankMaster.View);

        var response = await client.PutAsJsonAsync($"/api/banks/{Guid.NewGuid()}", new { code = "X", name = "X", isActive = true });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Activate_requires_activate_not_edit()
    {
        using var client = Client(Permissions.BankMaster.Edit);

        var response = await client.PostAsync($"/api/banks/{Guid.NewGuid()}/activate", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Delete_requires_edit_not_view()
    {
        using var client = Client(Permissions.BankMaster.View);

        var response = await client.DeleteAsync($"/api/banks/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Export_requires_export_permission()
    {
        using var client = Client(Permissions.BankMaster.View);

        var response = await client.GetAsync("/api/banks/export");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Import_validate_requires_import_permission()
    {
        using var client = Client(Permissions.BankMaster.View);
        using var form = new MultipartFormDataContent { { new StringContent("CreateOnly"), "mode" } };

        var response = await client.PostAsync("/api/bank-import/validate", form);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Import_history_requires_import_permission()
    {
        using var client = Client(Permissions.BankMaster.View);

        var response = await client.GetAsync("/api/bank-import/history");

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
