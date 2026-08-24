using System.Net;
using System.Net.Http.Json;
using HRMS.API.Controllers;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Domain.Entities;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Tests.TestSupport;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace HRMS.Tests;

/// <summary>
/// The diagnostic endpoint is anonymous by necessity — it has to work before anyone can sign in — so what
/// it says to an unauthenticated caller is a disclosure decision, not a formatting one.
/// <para>
/// It is also the one endpoint that reports on <em>every</em> organization, which since the split into a
/// database per organization means it cannot be a query any more. The development branch fans out, one scope
/// per shard, and both halves of that are asserted here: the numbers are per organization, and one broken
/// shard does not take the response down with it.
/// </para>
/// </summary>
public class SystemControllerTests : IClassFixture<HrmsApiFactory>
{
    private readonly HrmsApiFactory _factory;

    public SystemControllerTests(HrmsApiFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// Outside development the answer is liveness and nothing else — and it is produced without reading a
    /// database, which is what the two hostile stand-ins prove. A liveness check that queried a shard would
    /// report that shard's health as the API's own, and would fail at the apex where no shard is resolved.
    /// </summary>
    [Fact]
    public async Task Info_reports_only_liveness_outside_development()
    {
        var controller = new SystemController(
            new UnusableCatalog(),
            new UnusableScopeFactory(),
            new StubEnvironment("Production"),
            NullLogger<SystemController>.Instance);

        var info = await GetInfoAsync(controller);

        Assert.Equal("HRMS API", info.Application);
        Assert.NotEqual(default, info.UtcNow);

        // Platform size, the database it runs on and the customer list are all withheld: each tells an
        // unauthenticated caller something about the deployment they have no business knowing.
        Assert.Null(info.TenantCount);
        Assert.Null(info.UserCount);
        Assert.Null(info.RoleCount);
        Assert.Null(info.PermissionCount);
        Assert.Null(info.DatabaseProvider);
        Assert.Null(info.Tenants);
    }

    /// <summary>
    /// The development branch over the real pipeline, because the fan-out is a DI mechanism — a scope per
    /// organization, each picking its own shard — and a hand-built controller cannot exercise it.
    /// <para>
    /// Deliberately requested at the <em>apex</em>: the default client's <c>localhost</c> resolves to no
    /// organization, which is the host an operator actually types. Every other endpoint answers 404-ish
    /// there; this one has to answer in full, and it can only do that by taking its list from the catalog
    /// rather than from a tenant context it does not have.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Info_reports_every_organization_in_development_even_at_the_apex()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/system/info");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<SystemInfoResponse>>();
        Assert.NotNull(body?.Data);
        var info = body.Data;

        Assert.Equal(SeedData.Tenants.Count, info.TenantCount);
        Assert.Equal(SeedData.Users.Count, info.UserCount);
        Assert.NotNull(info.DatabaseProvider);
        Assert.NotNull(info.Tenants);
        Assert.Equal(["DEMO01", "DEMO02"], info.Tenants!.Select(tenant => tenant.TenantCode));

        // The per-organization figures are the seeded ones, not the platform total repeated. This is the
        // assertion that fails if the fan-out ever collapses back into a single unfiltered count — which is
        // exactly what it would look like in the shared-database mode, where every scope opens the same file.
        Assert.All(
            info.Tenants,
            tenant => Assert.Equal(
                SeedData.Users.Count(user => user.TenantId == TenantIdFor(tenant.TenantCode)),
                tenant.UserCount));

        // And they still add up, so nothing is double-counted or missed.
        Assert.Equal(info.UserCount, info.Tenants.Sum(tenant => tenant.UserCount));
    }

    /// <summary>
    /// Roles and permissions are platform-wide definitions replicated into every shard, so the endpoint
    /// reports one shard's counts as the platform's. Asserted against the seed rather than against another
    /// shard's numbers, which would be the same assertion twice.
    /// </summary>
    [Fact]
    public async Task Info_reports_the_replicated_role_and_permission_definitions()
    {
        using var client = _factory.CreateClient();

        var body = await client.GetFromJsonAsync<ApiResponse<SystemInfoResponse>>("/api/system/info");
        Assert.NotNull(body?.Data);

        Assert.Equal(SeedData.Roles.Count, body.Data.RoleCount);
        Assert.Equal(SeedData.Permissions.Count, body.Data.PermissionCount);
    }

    /// <summary>
    /// One organization whose database cannot be opened is reported as zero and logged, and every other
    /// organization in the same response is read normally. Built by hand because the failure has to be
    /// injected at the shard: a real host has no way to break one of its own SQLite files mid-test.
    /// <para>
    /// This is the behaviour an operator depends on. The endpoint is opened <em>because</em> something is
    /// wrong, and a response that failed as a whole would hide which shard is the broken one.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Info_reports_an_unreachable_shard_as_zero_rather_than_failing()
    {
        using var harness = await AuthTestHarness.CreateAsync();
        using var catalog = harness.Database.CreateCatalogContext();

        var controller = new SystemController(
            catalog,
            new UnusableScopeFactory(),
            new StubEnvironment("Development"),
            NullLogger<SystemController>.Instance);

        var info = await GetInfoAsync(controller);

        // The catalog was still readable, so the organizations are all listed …
        Assert.Equal(SeedData.Tenants.Count, info.TenantCount);
        Assert.NotNull(info.Tenants);
        Assert.Equal(["DEMO01", "DEMO02"], info.Tenants.Select(tenant => tenant.TenantCode));

        // … with no user counts, and no claim about a provider that was never reached.
        Assert.All(info.Tenants, tenant => Assert.Equal(0, tenant.UserCount));
        Assert.Equal(0, info.UserCount);
        Assert.Null(info.DatabaseProvider);
        Assert.Null(info.RoleCount);
        Assert.Null(info.PermissionCount);
    }

    private static Guid TenantIdFor(string tenantCode) => tenantCode switch
    {
        "DEMO01" => SeedData.TenantIds.Demo01,
        "DEMO02" => SeedData.TenantIds.Demo02,
        _ => throw new ArgumentOutOfRangeException(nameof(tenantCode), tenantCode, "Not a seeded organization.")
    };

    private static async Task<SystemInfoResponse> GetInfoAsync(SystemController controller)
    {
        var result = await controller.GetInfo(CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var body = Assert.IsType<ApiResponse<SystemInfoResponse>>(ok.Value);

        Assert.True(body.Success);
        Assert.NotNull(body.Data);
        return body.Data!;
    }

    /// <summary>Only the environment name matters here; the rest of IHostEnvironment is never read.</summary>
    private sealed class StubEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "HRMS.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    /// <summary>
    /// Throws on every member, so a test that expects no database access asserts it rather than assuming it.
    /// The exception message is the assertion failure a reader would want to see.
    /// </summary>
    private sealed class UnusableCatalog : IHrmsCatalogDbContext
    {
        private const string Reason = "The liveness answer must not read the catalog.";

        public DbSet<Tenant> Tenants => throw new InvalidOperationException(Reason);

        public DbSet<TenantBranding> TenantBranding => throw new InvalidOperationException(Reason);

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(Reason);
    }

    /// <summary>A scope factory that cannot produce a scope — every shard is unreachable through it.</summary>
    private sealed class UnusableScopeFactory : IServiceScopeFactory
    {
        public IServiceScope CreateScope() =>
            throw new InvalidOperationException("This shard's database cannot be opened.");
    }
}
