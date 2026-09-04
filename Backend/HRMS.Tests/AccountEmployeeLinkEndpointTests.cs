using System.Net;
using System.Net.Http.Headers;
using HRMS.Domain.Authorization;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Tests.TestSupport;

namespace HRMS.Tests;

public sealed class AccountEmployeeLinkEndpointTests : IClassFixture<HrmsApiFactory>
{
    private readonly HrmsApiFactory _factory;

    public AccountEmployeeLinkEndpointTests(HrmsApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Broad_admin_claims_do_not_bypass_live_link_grants()
    {
        using var client = _factory.CreateClientFor(HrmsApiFactory.Demo01Host);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            TestTokens.Create(
                SeedData.Users[0].Id,
                SeedData.TenantIds.Demo01,
                "DEMO01",
                "admin@demo01.com",
                roles: [RoleNames.TenantAdmin],
                permissions: Permissions.All));

        var response = await client.GetAsync(
            $"/api/account-employee-links/users/{SeedData.Users[1].Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
