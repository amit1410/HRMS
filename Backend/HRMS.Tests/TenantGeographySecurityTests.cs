using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Auth;
using HRMS.Application.DTOs.Masters;
using HRMS.Domain.Authorization;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Tests.TestSupport;

namespace HRMS.Tests;

/// <summary>
/// Regression coverage for the complete request boundary: host resolution, token tenant, requested
/// tenant resource, and tenant-filtered geography/organization masters must all agree.
/// </summary>
public class TenantGeographySecurityTests : IClassFixture<HrmsApiFactory>
{
    private static readonly Guid Demo02EmployeeId =
        OrganizationTestHarness.EmployeeId(SeedData.TenantIds.Demo02, "E-100");

    private readonly HrmsApiFactory _factory;

    public TenantGeographySecurityTests(HrmsApiFactory factory)
    {
        _factory = factory;
    }

    public static TheoryData<string> GeographyAndMasterRoutes => new()
    {
        "/api/countries",
        "/api/states",
        "/api/cities",
        "/api/master-data/work-locations?ActiveOnly=false",
        "/api/master-data/holding-companies?ActiveOnly=false"
    };

    [Theory]
    [MemberData(nameof(GeographyAndMasterRoutes))]
    public async Task Tenant_token_cannot_use_geography_or_master_endpoints_at_another_workspace(
        string route)
    {
        var token = await TokenForAsync(HrmsApiFactory.Demo01Host, "admin@demo01.com");
        using var client = ClientFor(HrmsApiFactory.Demo02Host, token);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(route)).StatusCode);
    }

    [Theory]
    [MemberData(nameof(GeographyAndMasterRoutes))]
    public async Task Tenant_token_cannot_use_geography_or_master_endpoints_at_an_unresolved_host(
        string route)
    {
        var token = await TokenForAsync(HrmsApiFactory.Demo01Host, "admin@demo01.com");
        using var client = ClientFor(HrmsApiFactory.UnknownHost, token);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(route)).StatusCode);
    }

    [Theory]
    [InlineData("/api/employees/{0}")]
    [InlineData("/api/employees/{0}/sensitive-details")]
    [InlineData("/api/employees/{0}/bank-details")]
    public async Task Tenant_cannot_read_another_tenants_employee_or_subresources(string routeTemplate)
    {
        var token = await TokenForAsync(HrmsApiFactory.Demo01Host, "admin@demo01.com");
        using var client = ClientFor(HrmsApiFactory.Demo01Host, token);

        var response = await client.GetAsync(string.Format(routeTemplate, Demo02EmployeeId));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Work_location_lookup_returns_only_the_current_tenants_rows()
    {
        var token = await TokenForAsync(HrmsApiFactory.Demo01Host, "admin@demo01.com");
        using var client = ClientFor(HrmsApiFactory.Demo01Host, token);

        var response = await client.GetAsync("/api/master-data/work-locations?ActiveOnly=false");
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<IReadOnlyList<MasterLookupDto>>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(body?.Data);
        Assert.Equal(
            SeedData.WorkLocations.Where(w => w.TenantId == SeedData.TenantIds.Demo01).Select(w => w.Id).Order(),
            body.Data.Select(w => w.Id).Order());
        Assert.DoesNotContain(
            body.Data,
            location => SeedData.WorkLocations.Any(w =>
                w.TenantId == SeedData.TenantIds.Demo02 && w.Id == location.Id));
    }

    [Fact]
    public async Task Geography_reads_require_the_geography_view_permission()
    {
        using var client = ClientFor(
            HrmsApiFactory.Demo01Host,
            TestTokens.Create(
                SeedData.Users[0].Id,
                SeedData.TenantIds.Demo01,
                "DEMO01",
                "admin@demo01.com",
                roles: [RoleNames.TenantAdmin],
                permissions: []));

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/countries")).StatusCode);
    }

    [Fact]
    public async Task Geography_mutation_requires_manage_permission_not_only_authentication_or_view()
    {
        var token = await TokenForAsync(HrmsApiFactory.Demo01Host, "hr@demo01.com");
        using var client = ClientFor(HrmsApiFactory.Demo01Host, token);

        var response = await client.PostAsJsonAsync("/api/countries", new
        {
            code = "ZZ",
            name = "Forbidden geography write",
            isActive = true
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private HttpClient ClientFor(string host, string accessToken)
    {
        var client = _factory.CreateClientFor(host);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    private async Task<string> TokenForAsync(string host, string email)
    {
        using var client = _factory.CreateClientFor(host);
        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email,
            password = SeedData.DefaultUserPassword
        });
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<LoginResponse>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return body!.Data!.AccessToken;
    }
}
