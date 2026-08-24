using System.Net;
using HRMS.Tests.TestSupport;

namespace HRMS.Tests;

/// <summary>
/// The CORS policy, checked through the real pipeline.
/// <para>
/// CORS is a browser-enforced contract, so a misconfiguration never shows up in a test that calls the API
/// with an HTTP client — it shows up as a request the browser refuses to hand back. Three parts of the policy
/// are load-bearing and are asserted here: an organization's own address is granted access without anyone
/// having configured it, hosts that merely resemble one are not, and <c>Content-Disposition</c> is exposed so
/// the CSV export keeps the filename the API chose. None of it is visible from the server's own responses
/// without setting an <c>Origin</c> header.
/// </para>
/// <para>
/// The full accept/refuse matrix lives in <see cref="CorsOriginPolicyTests"/>, which can vary the
/// configuration; this class asserts that the predicate is actually the one the pipeline consults, over the
/// origins the test host is configured with.
/// </para>
/// </summary>
public class CorsPolicyTests : IClassFixture<HrmsApiFactory>
{
    private readonly HrmsApiFactory _factory;

    public CorsPolicyTests(HrmsApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Preflight_from_the_dev_client_is_allowed_with_credentials()
    {
        using var client = _factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Options, "/api/auth/login");
        request.Headers.Add("Origin", HrmsApiFactory.ClientOrigin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type");

        var response = await client.SendAsync(request);

        Assert.Equal(HrmsApiFactory.ClientOrigin, Single(response, "Access-Control-Allow-Origin"));
        // The refresh flow sends the token in the body, but credentials stay allowed so a future move to
        // an HttpOnly cookie does not need a policy change as well.
        Assert.Equal("true", Single(response, "Access-Control-Allow-Credentials"));
    }

    /// <summary>
    /// <c>Content-Disposition</c> is not on the CORS-safelisted response header list, so without
    /// <c>WithExposedHeaders</c> the browser hides it and the export lands as "download" instead of
    /// <c>employees-2026-08-22.csv</c>. The header is missing from JavaScript's view even though it is
    /// present on the wire, which is why this is asserted on the policy rather than on the export endpoint.
    /// </summary>
    [Fact]
    public async Task Content_disposition_is_exposed_so_the_export_keeps_its_filename()
    {
        using var client = _factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/system/info");
        request.Headers.Add("Origin", HrmsApiFactory.ClientOrigin);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(
            "Content-Disposition",
            Values(response, "Access-Control-Expose-Headers"),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The whole reason the allow-list stopped being a list. Every organization has its own address, so an
    /// origin nobody has ever configured — a workspace onboarded five minutes ago — has to be granted access
    /// on the strength of its shape alone. Without this the CORS list would need an edit and a restart per
    /// customer, and the shortcut people reach for instead is to allow everything.
    /// </summary>
    [Theory]
    [InlineData("demo01")]
    [InlineData("demo02")]
    // Deliberately an organization that is not seeded and not registered in the catalog. CORS decides who may
    // *ask*; who the request belongs to is settled afterwards by host resolution, and conflating the two
    // would mean a browser could not even receive the "no such workspace" answer.
    [InlineData("never-provisioned")]
    public async Task An_organizations_own_address_is_granted_without_being_configured(string workspace)
    {
        var origin = HrmsApiFactory.WorkspaceOrigin(workspace);
        using var client = _factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/system/info");
        request.Headers.Add("Origin", origin);

        var response = await client.SendAsync(request);

        Assert.Equal(origin, Single(response, "Access-Control-Allow-Origin"));
        Assert.Equal("true", Single(response, "Access-Control-Allow-Credentials"));
    }

    /// <summary>
    /// The refusals, over the same host that accepts the origins above — so a policy that had quietly become
    /// permissive fails here rather than passing everything.
    /// <para>
    /// Each case is a host that a wildcard, a missing anchor or an unescaped dot would admit. A grant to any
    /// of them lets that page read every response this API sends the victim's browser, access tokens
    /// included, which is why they are asserted individually rather than as "some other origin".
    /// </para>
    /// </summary>
    [Theory]
    // Not ours at all — the case the old single-origin test covered, kept because it is still the baseline.
    [InlineData("https://not-our-client.example")]
    // Nested: a compromised host under a workspace label is not that workspace.
    [InlineData("http://a.b.localhost:5173")]
    [InlineData("http://staging.demo01.localhost:5173")]
    // Suffix confusion: our base domain is in there, but the origin belongs to somebody else.
    [InlineData("http://demo01.localhost.evil.test:5173")]
    [InlineData("http://localhost.evil.test:5173")]
    // Prefix confusion: the boundary character is a hyphen, so this is one label and not two.
    [InlineData("http://evil-localhost:5173")]
    // What an unescaped dot in a regular expression would have matched.
    [InlineData("http://demo01Xlocalhost:5173")]
    // The port is pinned by the template. The API's own port is not the client's.
    [InlineData("http://demo01.localhost:5080")]
    [InlineData("http://demo01.localhost")]
    // A scheme the templates do not name.
    [InlineData("ftp://demo01.localhost:5173")]
    // Sandboxed iframes, file:// documents and some redirects all send this.
    [InlineData("null")]
    public async Task An_origin_that_only_resembles_a_workspace_gets_no_grant(string origin)
    {
        using var client = _factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/system/info");
        request.Headers.Add("Origin", origin);

        var response = await client.SendAsync(request);

        // The response body is still produced — CORS is enforced in the browser, not here — but without the
        // grant header no page on that origin can read it.
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    /// <summary>
    /// A refused preflight is refused before the endpoint runs, so the browser gets no grant and the request
    /// it was asking permission for never happens. Asserted separately from the simple request above because
    /// the CORS middleware short-circuits this one, and "no grant" has to hold on both paths.
    /// </summary>
    [Fact]
    public async Task A_refused_preflight_grants_nothing()
    {
        using var client = _factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Options, "/api/auth/login");
        request.Headers.Add("Origin", "http://demo01.localhost.evil.test:5173");
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type");

        var response = await client.SendAsync(request);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(response.Headers.Contains("Access-Control-Allow-Methods"));
    }

    [Fact]
    public async Task A_request_with_no_origin_is_untouched_by_the_policy()
    {
        using var client = _factory.CreateClient();

        // Swagger, curl, health checks: not browser requests, so no CORS headers should be added.
        var response = await client.GetAsync("/api/system/info");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(response.Headers.Contains("Access-Control-Expose-Headers"));
    }

    private static string Single(HttpResponseMessage response, string header) =>
        Assert.Single(Values(response, header));

    private static string[] Values(HttpResponseMessage response, string header)
    {
        Assert.True(
            response.Headers.TryGetValues(header, out IEnumerable<string>? values),
            $"Expected the response to carry '{header}'. Headers: " +
            string.Join(", ", response.Headers.Select(h => h.Key)));

        // A single header may arrive as one comma-joined value; both forms are valid.
        return values!
            .SelectMany(value => value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .ToArray();
    }
}
