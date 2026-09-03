using System.Net;
using HRMS.Application.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace HRMS.Tests.TestSupport;

/// <summary>
/// Hosts the real API in-process — the actual authentication handler, authorization policies, validation
/// filter and middleware pipeline — over a throwaway SQLite file so nothing outside the test is touched.
/// <para>
/// Only configuration is overridden: the database provider, the signing key, and the rate limit (raised so
/// ordinary tests are not throttled; the throttling test constructs its own factory with a low limit).
/// </para>
/// </summary>
public sealed class HrmsApiFactory : WebApplicationFactory<Program>
{
    /// <summary>Held while a host is under construction; see <see cref="CreateHost"/> for why.</summary>
    private static readonly Lock HostBuildGate = new();

    /// <summary>Test-only signing key. Long enough for HMAC-SHA384/512 too, so algorithm-swap can be tested.</summary>
    public const string SigningKey =
        "integration-test-signing-key-long-enough-for-hmac-sha512-which-needs-sixty-four-bytes";

    public const string Issuer = "HRMS.API";
    public const string Audience = "HRMS.Client";

    /// <summary>
    /// The seeded organizations' own addresses. Which organization a request belongs to is the host it
    /// arrives at, so a test that wants to reach one has to say so here rather than in a request body.
    /// </summary>
    public const string Demo01Host = "http://demo01.localhost";

    public const string Demo02Host = "http://demo02.localhost";

    /// <summary>
    /// An address no organization is registered at. Distinct from the default <c>localhost</c>, which is
    /// also unregistered but reads as "no host was chosen" rather than as a deliberately wrong one.
    /// </summary>
    public const string UnknownHost = "http://nobody.localhost";

    /// <summary>
    /// The browser origin the client is served from when it is not at an organization's own address. Pinned
    /// here rather than read from the API's appsettings, because what the CORS policy allows is one of the
    /// things these tests assert — inheriting it would make the assertions agree with the configuration by
    /// construction.
    /// </summary>
    public const string ClientOrigin = "http://localhost:5173";

    /// <summary>The dev client's whitelabel origin shape; see <see cref="WorkspaceOrigin"/>.</summary>
    private const string WorkspaceOriginTemplate = "http://{workspace}.localhost:5173";

    private const string SecureWorkspaceOriginTemplate = "https://{workspace}.localhost:5173";

    /// <summary>
    /// One organization's browser origin. Deliberately a function of the label rather than a list, because
    /// the point of the policy is that an origin nobody configured is still allowed when it is shaped like a
    /// workspace — so tests need to be able to ask about a label that appears nowhere.
    /// </summary>
    public static string WorkspaceOrigin(string workspace) => $"http://{workspace}.localhost:5173";

    private readonly SqliteConnection _tenantConnection;

    /// <summary>
    /// The catalog gets its own named in-memory database, not a second table in the one above. Sharing a
    /// database would let a query join across the catalog/tenant boundary and pass here while failing
    /// against two real databases.
    /// </summary>
    private readonly SqliteConnection _catalogConnection;

    private readonly int _authPermitLimit;

    /// <summary>
    /// The address every request appears to arrive from, or null to leave the test host's own value — which
    /// is no address at all. See <see cref="BehindALoopbackProxy"/> and <see cref="ReachedDirectlyFrom"/>.
    /// </summary>
    private readonly IPAddress? _remoteAddress;

    /// <summary>
    /// xUnit requires a class fixture to expose exactly one public constructor, so alternate limits are
    /// reached through <see cref="WithAuthPermitLimit"/> rather than a second constructor.
    /// </summary>
    public HrmsApiFactory() : this(10_000)
    {
    }

    private HrmsApiFactory(int authPermitLimit, IPAddress? remoteAddress = null)
    {
        _authPermitLimit = authPermitLimit;
        _remoteAddress = remoteAddress;

        // Keep the named in-memory databases alive for the entire fixture. Each EF context opened by the
        // application connects to these databases, while the anchor connections prevent SQLite from
        // discarding the schema between scopes. They are test-owned and cannot point at local HRMS files.
        _tenantConnection = new SqliteConnection(
            $"Data Source=hrms-integration-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _catalogConnection = new SqliteConnection(
            $"Data Source=hrms-integration-catalog-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _tenantConnection.Open();
        _catalogConnection.Open();
    }

    /// <summary>A host whose credential endpoints permit only <paramref name="permitLimit"/> requests per window.</summary>
    public static HrmsApiFactory WithAuthPermitLimit(int permitLimit) => new(permitLimit);

    /// <summary>
    /// A host whose caller is a trusted proxy, so <c>X-Forwarded-*</c> headers are honoured. Loopback because
    /// that is what the forwarded-headers defaults trust without any configuration.
    /// <para>
    /// <see cref="Microsoft.AspNetCore.TestHost.TestServer"/> presents no remote address at all, so without
    /// one of these two helpers a test cannot say which side of the trust boundary it is on. Both directions
    /// matter: honouring <c>X-Forwarded-Host</c> is what lets a real deployment resolve an organization, and
    /// honouring it from an untrusted caller would let anyone name one.
    /// </para>
    /// </summary>
    public static HrmsApiFactory BehindALoopbackProxy(int permitLimit = 10_000) =>
        new(permitLimit, IPAddress.Loopback);

    /// <summary>
    /// A host reached directly from <paramref name="address"/> — an address no <c>ForwardedHeaders</c> setting
    /// names, and outside the loopback range the defaults trust. Whatever such a caller says about the host,
    /// the scheme or the client address has to be ignored.
    /// </summary>
    public static HrmsApiFactory ReachedDirectlyFrom(string address, int permitLimit = 10_000) =>
        new(permitLimit, IPAddress.Parse(address));

    /// <summary>
    /// A client addressed to one organization's host. The plain <see cref="WebApplicationFactory{T}.CreateClient()"/>
    /// uses <c>localhost</c>, which resolves to no organization — so anything that has to sign in, or to read
    /// tenant data, needs one of these instead.
    /// </summary>
    public HttpClient CreateClientFor(string host) =>
        CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri(host) });

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);

        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Database:Provider"] = "Sqlite",
                ["ConnectionStrings:Sqlite"] = _tenantConnection.ConnectionString,
                ["ConnectionStrings:SqliteCatalog"] = _catalogConnection.ConnectionString,
                // The test databases are deliberately initialized by the normal startup path. This explicit
                // false value prevents an inherited Database__SkipInitialization environment variable from
                // leaving the isolated catalog without its Tenants table.
                ["Database:SkipInitialization"] = "false",
                ["Jwt:Issuer"] = Issuer,
                ["Jwt:Audience"] = Audience,
                ["Jwt:SecretKey"] = SigningKey,
                ["Jwt:AccessTokenMinutes"] = "30",
                ["Jwt:RefreshTokenDays"] = "7",
                ["Jwt:ClockSkewSeconds"] = "0",
                ["RateLimiting:Authentication:PermitLimit"] = _authPermitLimit.ToString(),
                ["RateLimiting:Authentication:WindowSeconds"] = "60",
                // Both indices of each list are set, not just the first: configuration merges arrays by
                // index, so overriding only ":0" would leave the API's own ":1" in place and the effective
                // set would be half these values and half whichever appsettings file loaded.
                ["Cors:AllowedOrigins:0"] = ClientOrigin,
                ["Cors:AllowedOrigins:1"] = "https://localhost:5173",
                ["Cors:WorkspaceOriginTemplates:0"] = WorkspaceOriginTemplate,
                ["Cors:WorkspaceOriginTemplates:1"] = SecureWorkspaceOriginTemplate,
                // Keep the test output readable; the API's own logging is exercised elsewhere.
                ["Serilog:MinimumLevel:Default"] = "Warning",
                ["Serilog:MinimumLevel:Override:Microsoft.AspNetCore"] = "Warning",
                ["Serilog:MinimumLevel:Override:Microsoft.EntityFrameworkCore.Database.Command"] = "Warning"
            }));

        if (_remoteAddress is not null)
        {
            var address = _remoteAddress;
            builder.ConfigureServices(services =>
                services.AddTransient<IStartupFilter>(_ => new RemoteAddressFilter(address)));
        }
    }

    /// <summary>
    /// Stamps a remote address on every request before the app's own pipeline runs, because the test host
    /// leaves it unset.
    /// <para>
    /// A startup filter rather than <c>builder.Configure</c>, because this has to sit ahead of
    /// <c>UseForwardedHeaders</c> — which is itself the first thing in the pipeline after exception handling —
    /// and a filter's middleware wraps everything the app registers.
    /// </para>
    /// </summary>
    private sealed class RemoteAddressFilter(IPAddress address) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = address;
                await nextMiddleware();
            });

            next(app);
        };
    }

    /// <summary>
    /// Verifies the overrides above actually reached the app. These are layered after the API's own
    /// appsettings files, so anything that reads configuration while services are still being registered
    /// would silently keep the development values — the host would then sign tokens with one key and
    /// validate them with another. Failing here names the cause instead of leaving 401s to explain.
    /// <para>
    /// Construction is serialized across factories. Hosting an entry point that uses top-level statements
    /// works by starting <c>Main</c> and intercepting a process-wide diagnostic event to capture the host it
    /// builds. That handshake has no way to tell two concurrent builds apart, so when two test classes spin
    /// up a host at the same moment one of them can be handed the other's event and fail with "the entry
    /// point exited without ever building an IHost". The gate is only held while a host is being built;
    /// tests themselves still run in parallel.
    /// </para>
    /// </summary>
    protected override IHost CreateHost(IHostBuilder builder)
    {
        IHost host;
        lock (HostBuildGate)
        {
            host = base.CreateHost(builder);
        }

        var settings = host.Services.GetRequiredService<IOptions<JwtSettings>>().Value;
        if (settings.SecretKey != SigningKey)
        {
            throw new InvalidOperationException(
                "The integration host did not pick up the test JWT configuration: something reads the " +
                "'Jwt' section before the test configuration source is layered in.");
        }

        var configuration = host.Services.GetRequiredService<IConfiguration>();
        if (configuration.GetValue<bool>("Database:SkipInitialization"))
        {
            throw new InvalidOperationException(
                "The integration host unexpectedly inherited Database:SkipInitialization; its isolated " +
                "catalog and tenant databases were not prepared.");
        }

        return host;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
        {
            return;
        }

        // Release pooled handles before closing the anchors so SQLite discards the test databases.
        SqliteConnection.ClearAllPools();
        _tenantConnection.Dispose();
        _catalogConnection.Dispose();
    }
}
