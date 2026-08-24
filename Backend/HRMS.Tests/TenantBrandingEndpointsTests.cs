using System.Net;
using System.Net.Http.Json;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Tenants;
using HRMS.Infrastructure.Persistence.Catalog;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HRMS.Tests;

/// <summary>
/// The branding endpoint over the real pipeline. What only shows up here is that it is reachable without a
/// token at all, that the route carries no organization identifier, and that it shares the credential
/// endpoints' rate-limit bucket.
/// <para>
/// The service tests cover the response's content. These cover its reachability, because an endpoint that
/// answers anonymously is the one place where getting the routing wrong is a disclosure rather than a bug.
/// </para>
/// </summary>
public class TenantBrandingEndpointsTests : IClassFixture<HrmsApiFactory>
{
    private const string Route = "/api/tenants/current/branding";

    private readonly HrmsApiFactory _factory;

    public TenantBrandingEndpointsTests(HrmsApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Branding_is_readable_without_a_token_at_an_organizations_address()
    {
        using var client = _factory.CreateClientFor(HrmsApiFactory.Demo01Host);

        var response = await client.GetAsync(Route);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(client.DefaultRequestHeaders.Authorization);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<TenantBrandingDto>>();
        Assert.NotNull(body);
        Assert.True(body.Success);
        Assert.Equal("Demo Organization", body.Data!.DisplayName);
        Assert.Equal("#0F766E", body.Data.PrimaryColor);
    }

    /// <summary>
    /// The host is the whole input. Two addresses, one route, two answers — and if the middleware did not run
    /// ahead of this endpoint, both would be the empty response instead.
    /// </summary>
    [Fact]
    public async Task The_address_decides_which_organizations_branding_is_served()
    {
        using var demo01 = _factory.CreateClientFor(HrmsApiFactory.Demo01Host);
        using var demo02 = _factory.CreateClientFor(HrmsApiFactory.Demo02Host);

        var first = await ReadAsync(demo01);
        var second = await ReadAsync(demo02);

        Assert.Equal("Demo Organization", first!.DisplayName);
        Assert.Equal("Sample Organization", second!.DisplayName);
    }

    /// <summary>
    /// The apex host and any unregistered address get a 200 with nothing in it. Not a 404: this endpoint is
    /// anonymous, so a status code that distinguished a registered address from an unregistered one would be
    /// a way to ask the question without ever signing in.
    /// </summary>
    [Fact]
    public async Task An_unregistered_address_gets_an_empty_success_rather_than_a_404()
    {
        using var client = _factory.CreateClientFor(HrmsApiFactory.UnknownHost);

        var response = await client.GetAsync(Route);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<TenantBrandingDto>>();
        Assert.True(body!.Success);
        Assert.Null(body.Data!.DisplayName);
        Assert.Null(body.Data.PrimaryColor);
        Assert.False(body.Data.SsoEnabled);
    }

    /// <summary>
    /// The opt-in asserted on the wire, byte for byte, which is the only level at which "indistinguishable"
    /// means anything: two deserialized DTOs can compare equal while the JSON differs in a field name or a
    /// null that one body omits and the other spells out.
    /// <para>
    /// DEMO02 is used here and nowhere else in this class, because the edit persists for the rest of the
    /// fixture's life.
    /// </para>
    /// </summary>
    [Fact]
    public async Task An_organization_that_has_opted_out_returns_the_same_bytes_as_an_unregistered_address()
    {
        await SetBrandingPublicAsync(TestShards.Demo02Host, isPublic: false);

        using var optedOut = _factory.CreateClientFor(HrmsApiFactory.Demo02Host);
        using var unregistered = _factory.CreateClientFor(HrmsApiFactory.UnknownHost);

        var optedOutBody = await (await optedOut.GetAsync(Route)).Content.ReadAsStringAsync();
        var unregisteredBody = await (await unregistered.GetAsync(Route)).Content.ReadAsStringAsync();

        Assert.Equal(unregisteredBody, optedOutBody);
        Assert.DoesNotContain("DEMO02", optedOutBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Sample", optedOutBody, StringComparison.Ordinal);
    }

    /// <summary>
    /// No response names an organization: no code, no id, no shard key, and no field that could carry one.
    /// The service tests pin the DTO's shape; this pins what actually goes over the wire, envelope included.
    /// <para>
    /// The organization's own code is deliberately not searched for as a substring, because branding it has
    /// chosen to publish may legitimately contain it — the seeded support address is
    /// <c>itsupport@demo01.com</c>. What must not appear is an identifier the visitor never asked for and was
    /// never shown.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_response_carries_no_organization_identifier()
    {
        using var client = _factory.CreateClientFor(HrmsApiFactory.Demo01Host);

        var raw = await (await client.GetAsync(Route)).Content.ReadAsStringAsync();

        Assert.DoesNotContain("tenantCode", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tenantId", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("shardKey", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(SeedData.TenantIds.Demo01.ToString(), raw, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The route that took an organization code is gone, not merely unused. Leaving it in place would keep the
    /// oracle the new route exists to remove — a caller could work through candidate codes at an address that
    /// has nothing to do with any of them.
    /// <para>
    /// <c>401</c> rather than <c>404</c>, and that is the fallback authorization policy rather than an
    /// accident: it closes paths that match no endpoint at all, so a retired route does not even confirm it
    /// was ever there. The body is checked too, since a route re-added behind authentication would still be
    /// answering the question this endpoint exists to stop answering.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("/api/tenants/DEMO01/branding")]
    [InlineData("/api/tenants/DEMO02/branding")]
    [InlineData("/api/tenants/branding")]
    public async Task There_is_no_route_that_takes_an_organization_code(string route)
    {
        using var client = _factory.CreateClientFor(HrmsApiFactory.Demo01Host);

        var response = await client.GetAsync(route);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain(
            "Demo Organization", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Branding shares the credential endpoints' limiter. It is anonymous and it reaches the catalog on every
    /// call, so without that it would be the cheapest way to load the one database every organization's
    /// sign-in depends on.
    /// </summary>
    [Fact]
    public async Task Reading_branding_repeatedly_is_throttled_by_the_credential_limiter()
    {
        using var throttled = HrmsApiFactory.WithAuthPermitLimit(3);
        using var client = throttled.CreateClientFor(HrmsApiFactory.Demo01Host);

        var statuses = new List<HttpStatusCode>();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            statuses.Add((await client.GetAsync(Route)).StatusCode);
        }

        Assert.Equal(3, statuses.Count(s => s == HttpStatusCode.OK));
        Assert.Equal(2, statuses.Count(s => s == HttpStatusCode.TooManyRequests));
    }

    private static async Task<TenantBrandingDto?> ReadAsync(HttpClient client)
    {
        var response = await client.GetAsync(Route);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<TenantBrandingDto>>();
        return body!.Data;
    }

    private async Task SetBrandingPublicAsync(string host, bool isPublic)
    {
        using var scope = _factory.Services.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<HrmsCatalogDbContext>();

        (await catalog.TenantBranding.SingleAsync(b => b.Tenant!.Host == host)).IsPublic = isPublic;
        await catalog.SaveChangesAsync();
    }
}
