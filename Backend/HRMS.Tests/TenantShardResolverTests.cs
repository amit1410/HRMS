using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence.Catalog;
using HRMS.Infrastructure.Sharding;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace HRMS.Tests;

/// <summary>
/// Host to organization, read from the catalog. This is the lookup that has to succeed before any tenant
/// database can be opened, so it runs on every request — which makes both its correctness and its cache
/// load-bearing.
/// </summary>
public class TenantShardResolverTests : IDisposable
{
    private readonly SqliteInMemoryDatabase _database = new();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    [Fact]
    public async Task Resolves_a_seeded_host_to_its_organization()
    {
        await _database.SeedAsync();
        using var catalog = _database.CreateCatalogContext();

        var shard = await CreateResolver(catalog).ResolveByHostAsync("demo01.localhost");

        Assert.NotNull(shard);
        Assert.Equal("DEMO01", shard.TenantCode);
        Assert.Equal("demo01", shard.ShardKey);
        Assert.Equal("demo01.localhost", shard.Host);
        Assert.Equal(TenantStatus.Active, shard.Status);
        Assert.NotEqual(Guid.Empty, shard.TenantId);
    }

    /// <summary>
    /// Hosts are case-insensitive and may carry the root label's trailing dot. Both forms are the same
    /// workspace to whoever typed them, so both have to be the same workspace here — and normalizing in one
    /// place is what lets the stored column stay a plain unique index with an exact match, which then behaves
    /// identically on SQL Server's case-insensitive default collation and on SQLite's case-sensitive one.
    /// </summary>
    [Theory]
    [InlineData("DEMO01.LOCALHOST")]
    [InlineData("Demo01.Localhost")]
    [InlineData("demo01.localhost.")]
    [InlineData("  demo01.localhost  ")]
    public async Task Resolves_a_host_whatever_case_or_trailing_dot_it_arrives_in(string host)
    {
        await _database.SeedAsync();
        using var catalog = _database.CreateCatalogContext();

        var shard = await CreateResolver(catalog).ResolveByHostAsync(host);

        Assert.Equal("DEMO01", shard?.TenantCode);
    }

    [Fact]
    public async Task A_host_no_organization_signs_in_at_resolves_to_nothing()
    {
        await _database.SeedAsync();
        using var catalog = _database.CreateCatalogContext();

        Assert.Null(await CreateResolver(catalog).ResolveByHostAsync("nobody.localhost"));
    }

    /// <summary>
    /// Answered without touching the catalog — proved by disposing the context the resolver was built on,
    /// which would throw the moment a query ran. A host that is empty or longer than the 253-character column
    /// cannot match any row, so serving it a database round trip would just be a free query for whoever sent
    /// the header.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".")]
    public async Task An_impossible_host_is_refused_without_querying_the_catalog(string host)
    {
        var catalog = _database.CreateCatalogContext();
        var resolver = CreateResolver(catalog);
        catalog.Dispose();

        Assert.Null(await resolver.ResolveByHostAsync(host));
    }

    [Fact]
    public async Task A_host_longer_than_the_column_is_refused_without_querying_the_catalog()
    {
        var catalog = _database.CreateCatalogContext();
        var resolver = CreateResolver(catalog);
        catalog.Dispose();

        Assert.Null(await resolver.ResolveByHostAsync(new string('a', 254)));
    }

    /// <summary>
    /// An organization that is switched off still resolves. The status travels with the descriptor so the
    /// caller can refuse the request and say which organization it refused — a resolver that filtered inactive
    /// rows out would make a suspended workspace indistinguishable from one that never existed, in the logs as
    /// well as in the response.
    /// </summary>
    [Fact]
    public async Task An_inactive_organization_still_resolves_so_the_caller_can_refuse_it()
    {
        await _database.SeedAsync();
        await SuspendAsync("DEMO02");

        using var catalog = _database.CreateCatalogContext();

        var shard = await CreateResolver(catalog).ResolveByHostAsync("demo02.localhost");

        Assert.Equal(TenantStatus.Suspended, shard?.Status);
    }

    /// <summary>
    /// The cache is what stops one catalog query per request from making the catalog the first thing to fall
    /// over under load. Asserted by changing the catalog underneath it and showing the old answer still comes
    /// back — the same staleness window that means a suspended organization keeps being served for a few more
    /// seconds, which is why the TTL is short rather than generous.
    /// </summary>
    [Fact]
    public async Task Remembers_a_resolved_host_rather_than_asking_the_catalog_again()
    {
        await _database.SeedAsync();
        using var catalog = _database.CreateCatalogContext();
        var resolver = CreateResolver(catalog);

        Assert.Equal("demo01", (await resolver.ResolveByHostAsync("demo01.localhost"))?.ShardKey);

        await using (var edited = _database.CreateCatalogContext())
        {
            (await edited.Tenants.SingleAsync(t => t.TenantCode == "DEMO01")).ShardKey = "moved";
            await edited.SaveChangesAsync();
        }

        Assert.Equal("demo01", (await resolver.ResolveByHostAsync("demo01.localhost"))?.ShardKey);

        // A resolver with a cold cache reads the change, so the value really did move in the catalog and the
        // assertion above is about the cache rather than about a write that never landed.
        using var coldCatalog = _database.CreateCatalogContext();
        Assert.Equal("moved", (await CreateResolver(coldCatalog, ColdCache()).ResolveByHostAsync("demo01.localhost"))?.ShardKey);
    }

    /// <summary>
    /// Misses are cached too, and that is the more important half: unknown hosts are the traffic an attacker
    /// gets to choose, so without this a flood of requests to hosts nobody owns is a flood of catalog queries.
    /// </summary>
    [Fact]
    public async Task Remembers_that_a_host_resolved_to_nothing()
    {
        await _database.SeedAsync();
        using var catalog = _database.CreateCatalogContext();
        var resolver = CreateResolver(catalog);

        Assert.Null(await resolver.ResolveByHostAsync("later.localhost"));

        await using (var added = _database.CreateCatalogContext())
        {
            added.Tenants.Add(new Tenant
            {
                Id = Guid.NewGuid(),
                TenantCode = "LATER1",
                Host = "later.localhost",
                ShardKey = "later",
                TenantName = "Added After The Miss",
                Status = TenantStatus.Active
            });
            await added.SaveChangesAsync();
        }

        Assert.Null(await resolver.ResolveByHostAsync("later.localhost"));

        using var coldCatalog = _database.CreateCatalogContext();
        Assert.Equal(
            "LATER1",
            (await CreateResolver(coldCatalog, ColdCache()).ResolveByHostAsync("later.localhost"))?.TenantCode);
    }

    private TenantShardResolver CreateResolver(HrmsCatalogDbContext catalog, IMemoryCache? cache = null) =>
        new(catalog, cache ?? _cache, Options.Create(new ShardingOptions()));

    private static MemoryCache ColdCache() => new(new MemoryCacheOptions());

    private async Task SuspendAsync(string tenantCode)
    {
        await using var catalog = _database.CreateCatalogContext();
        (await catalog.Tenants.SingleAsync(t => t.TenantCode == tenantCode)).Status = TenantStatus.Suspended;
        await catalog.SaveChangesAsync();
    }

    public void Dispose()
    {
        _cache.Dispose();
        _database.Dispose();
    }
}
