using System.Net;
using System.Net.Http.Json;
using HRMS.Application.Common;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence.Catalog;
using HRMS.Tests.TestSupport;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HRMS.Tests;

/// <summary>
/// Host-to-organization routing, through the real pipeline.
/// <para>
/// The unit tests cover the resolver and the connection-string seam in isolation. What can only be checked
/// here is the <em>ordering</em>: that host resolution runs ahead of authentication, so a switched-off
/// organization is refused before a token is ever examined, and that a host belonging to no organization is
/// still served — the apex host and the health probe both depend on that.
/// </para>
/// </summary>
public class TenantHostRoutingTests : IClassFixture<HrmsApiFactory>
{
    /// <summary>Seeded in <c>SeedData</c>; every label under <c>localhost</c> resolves to the loopback.</summary>
    private const string ActiveHost = "http://demo01.localhost";

    /// <summary>
    /// DEMO02 belongs to the suspension test alone. The resolver's cache is a host singleton, so an earlier
    /// request to this host would pin the Active descriptor for the length of the TTL and the suspension
    /// below would appear not to take effect.
    /// </summary>
    private const string SuspendedHost = "http://demo02.localhost";

    /// <summary>An authenticated route, so a 401 is the answer whenever authentication gets to run.</summary>
    private const string AuthenticatedRoute = "/api/employees";

    private readonly HrmsApiFactory _factory;

    public TenantHostRoutingTests(HrmsApiFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// The ordering assertion. 401 here would mean authentication ran first and the organization's status was
    /// never consulted — a suspended workspace would then still be reachable by anyone holding a valid token.
    /// 404 means the request was turned away at the edge, before any route, on the host alone.
    /// </summary>
    [Fact]
    public async Task A_suspended_organizations_host_is_refused_before_authentication_runs()
    {
        await SetStatusAsync("DEMO02", TenantStatus.Suspended);

        using var client = ClientFor(SuspendedHost);

        var response = await client.GetAsync(AuthenticatedRoute);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// The refusal says nothing about the organization. Whoever is looking learns that nothing is served at
    /// this host, not that a workspace exists here and has been switched off.
    /// </summary>
    [Fact]
    public async Task The_refusal_names_no_organization_and_gives_no_reason()
    {
        await SetStatusAsync("DEMO02", TenantStatus.Suspended);

        using var client = ClientFor(SuspendedHost);

        var response = await client.GetAsync(AuthenticatedRoute);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse>();

        Assert.NotNull(body);
        Assert.False(body.Success);
        Assert.Equal("This workspace is not available.", body.Message);
        Assert.Null(body.Errors);

        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("DEMO02", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("suspend", raw, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A host no organization signs in at is not an error. The apex host serves the workspace picker and
    /// <c>/health</c> answers the load balancer, and both arrive on a host that resolves to nothing — so
    /// refusing here would take them down. Nothing tenant-scoped can proceed regardless: there is no shard
    /// for a token to agree with, and opening a tenant database without one is refused rather than defaulted.
    /// </summary>
    [Fact]
    public async Task A_host_belonging_to_no_organization_is_still_served()
    {
        // The factory's default base address is plain "localhost", which is not a seeded workspace host.
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/system/info");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// And an unresolved host does not become a way past authentication either — the authenticated route
    /// answers 401, decided by the authentication handler rather than by this middleware.
    /// </summary>
    [Fact]
    public async Task An_unresolved_host_does_not_open_an_authenticated_route()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(AuthenticatedRoute);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// The transparency check for the common case: an active organization's host changes nothing about how a
    /// route behaves. Without this, a middleware that refused everything would still pass the tests above.
    /// </summary>
    [Fact]
    public async Task An_active_organizations_host_leaves_the_pipeline_alone()
    {
        using var client = ClientFor(ActiveHost);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/system/info")).StatusCode);

        // 401 rather than 404: the host resolved, so the route was reached and authentication answered it.
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(AuthenticatedRoute)).StatusCode);
    }

    /// <summary>
    /// The base address becomes the request's Host header, which is the only input host resolution has.
    /// </summary>
    private HttpClient ClientFor(string host) =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri(host) });

    private async Task SetStatusAsync(string tenantCode, TenantStatus status)
    {
        using var scope = _factory.Services.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<HrmsCatalogDbContext>();

        (await catalog.Tenants.SingleAsync(tenant => tenant.TenantCode == tenantCode)).Status = status;
        await catalog.SaveChangesAsync();
    }
}
