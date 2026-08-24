using System.Net;
using System.Net.Http.Json;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Tenants;
using HRMS.Tests.TestSupport;

namespace HRMS.Tests;

/// <summary>
/// What this API believes when a proxy rewrites the client address, the scheme and the host.
/// <para>
/// This stopped being a logging nicety the moment the host chose the database. <c>X-Forwarded-Host</c> now
/// selects a tenant's shard, so the question "who is allowed to set it" is the question "who is allowed to
/// name an organization" — and the answer has to be "only a proxy this deployment listed". Both directions are
/// asserted below, because either one alone is a false sense of security: honouring the header from anyone is
/// an authorization hole, and honouring it from nobody is a deployment that cannot resolve a tenant at all.
/// </para>
/// <para>
/// Every test here fixes the remote address explicitly, through <see cref="HrmsApiFactory.BehindALoopbackProxy"/>
/// or <see cref="HrmsApiFactory.ReachedDirectlyFrom"/>. The test host presents no remote address of its own, and
/// with nothing to compare against the middleware has no trust decision to make — so a test that left the
/// address unset would be asserting the behaviour of neither side of the boundary.
/// </para>
/// </summary>
public class ForwardedHeadersTests
{
    private const string BrandingRoute = "/api/tenants/current/branding";

    /// <summary>The address the header claims the request arrived at. Registered, and not the one used.</summary>
    private const string ClaimedHost = "demo01.localhost";

    /// <summary>
    /// An address outside the loopback range the forwarded-headers defaults trust, and named by no
    /// <c>ForwardedHeaders</c> setting the test host sets. A caller reaching the API from here is the internet,
    /// not the proxy.
    /// </summary>
    private const string UntrustedCaller = "198.51.100.7";

    /// <summary>
    /// The one that matters. A request that reaches this API from an address it does not trust may say
    /// whatever it likes about the host — and is answered as the address it actually arrived at.
    /// <para>
    /// If this ever fails, anyone who can reach the API directly can name any organization's host and be
    /// routed to that organization's database, with no credential involved in the choice.
    /// </para>
    /// </summary>
    [Fact]
    public async Task An_untrusted_caller_cannot_choose_an_organization_with_a_header()
    {
        using var factory = HrmsApiFactory.ReachedDirectlyFrom(UntrustedCaller);
        using var client = factory.CreateClientFor(HrmsApiFactory.UnknownHost);
        client.DefaultRequestHeaders.Add("X-Forwarded-Host", ClaimedHost);

        var branding = await ReadBrandingAsync(client);

        // The neutral answer an unregistered address gets: the header was ignored, so the request is still at
        // nobody.localhost.
        Assert.Null(branding.DisplayName);
        Assert.Null(branding.PrimaryColor);
    }

    /// <summary>
    /// The same refusal, on the header the rate limiter reads. Three requests that differ in nothing but a
    /// forwarded address they are not entitled to set share one budget, because the partition key is the
    /// address the request really came from.
    /// <para>
    /// Honouring this header from an untrusted caller would make the credential rate limiter decorative: a
    /// password-guessing client would spend one request per invented address and never be throttled at all.
    /// </para>
    /// </summary>
    [Fact]
    public async Task An_untrusted_caller_cannot_spend_someone_elses_rate_limit_budget()
    {
        using var factory = HrmsApiFactory.ReachedDirectlyFrom(UntrustedCaller, permitLimit: 2);

        var statuses = await ReadOncePerAddressAsync(factory, "203.0.113.20", "203.0.113.21", "203.0.113.22");

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], statuses);
    }

    /// <summary>
    /// The other direction, and the reason the middleware is in the pipeline at all: behind a trusted proxy
    /// the forwarded host is the real one, because the address the request physically arrived at is an
    /// internal service name that belongs to no organization.
    /// </summary>
    [Fact]
    public async Task A_trusted_proxy_can_say_which_address_the_browser_asked_for()
    {
        using var factory = HrmsApiFactory.BehindALoopbackProxy();
        using var client = factory.CreateClientFor(HrmsApiFactory.UnknownHost);
        client.DefaultRequestHeaders.Add("X-Forwarded-Host", ClaimedHost);

        var branding = await ReadBrandingAsync(client);

        Assert.Equal("Demo Organization", branding.DisplayName);
    }

    /// <summary>
    /// Without the forwarded host, a trusted proxy resolves nothing — which is the same host, the same trust,
    /// and only the header removed. Pins the previous test's result to the header rather than to the proxy
    /// simulation having somehow changed the address itself.
    /// </summary>
    [Fact]
    public async Task A_trusted_proxy_that_forwards_no_host_resolves_nothing()
    {
        using var factory = HrmsApiFactory.BehindALoopbackProxy();
        using var client = factory.CreateClientFor(HrmsApiFactory.UnknownHost);

        var branding = await ReadBrandingAsync(client);

        Assert.Null(branding.DisplayName);
    }

    /// <summary>
    /// <c>X-Forwarded-For</c> reaching the rate limiter, observed the only way it can be from outside: two
    /// clients that differ in nothing but the forwarded address get their own budgets.
    /// <para>
    /// Behind a load balancer without this, every request in the world shares one partition keyed on the
    /// balancer's address — so one organization's sign-in traffic locks out everybody else's, and a single
    /// attacker exhausts the bucket for every tenant at once. That failure looks like a working rate limiter
    /// right up until it happens.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_rate_limiter_partitions_on_the_forwarded_client_address()
    {
        using var factory = HrmsApiFactory.BehindALoopbackProxy(permitLimit: 2);

        var first = await ReadRepeatedlyAsync(factory, "203.0.113.10", attempts: 3);
        var second = await ReadOncePerAddressAsync(factory, "203.0.113.11");

        // The third call from the first address is over its limit.
        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], first);

        // The second address is unaffected, which is only true if its partition key differs.
        Assert.Equal([HttpStatusCode.OK], second);
    }

    private static async Task<List<HttpStatusCode>> ReadRepeatedlyAsync(
        HrmsApiFactory factory, string clientAddress, int attempts)
    {
        using var client = factory.CreateClientFor(HrmsApiFactory.Demo01Host);
        client.DefaultRequestHeaders.Add("X-Forwarded-For", clientAddress);

        var statuses = new List<HttpStatusCode>();
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            statuses.Add((await client.GetAsync(BrandingRoute)).StatusCode);
        }

        return statuses;
    }

    /// <summary>
    /// One request per forwarded address, each on its own client so nothing but the header varies. Sequential
    /// on purpose: the assertions are about which request crosses a limit, which needs an order.
    /// </summary>
    private static async Task<List<HttpStatusCode>> ReadOncePerAddressAsync(
        HrmsApiFactory factory, params string[] clientAddresses)
    {
        var statuses = new List<HttpStatusCode>();
        foreach (var clientAddress in clientAddresses)
        {
            using var client = factory.CreateClientFor(HrmsApiFactory.Demo01Host);
            client.DefaultRequestHeaders.Add("X-Forwarded-For", clientAddress);
            statuses.Add((await client.GetAsync(BrandingRoute)).StatusCode);
        }

        return statuses;
    }

    private static async Task<TenantBrandingDto> ReadBrandingAsync(HttpClient client)
    {
        var response = await client.GetAsync(BrandingRoute);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<TenantBrandingDto>>();
        Assert.NotNull(body?.Data);
        return body.Data;
    }
}
