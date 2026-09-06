using HRMS.Application.Abstractions;
using HRMS.Domain.Enums;
using HRMS.Infrastructure;
using HRMS.Infrastructure.Persistence;
using HRMS.Infrastructure.Persistence.Catalog;
using HRMS.Infrastructure.Sharding;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HRMS.Tests;

public sealed class DatabaseProviderRoutingTests : IDisposable
{
    private readonly SqliteInMemoryDatabase _database = new();

    [Fact]
    public async Task Existing_seeded_tenant_defaults_to_sql_server()
    {
        await _database.SeedAsync();
        using var catalog = _database.CreateCatalogContext();
        var resolver = new TenantShardResolver(catalog, new MemoryCache(new MemoryCacheOptions()), Options.Create(new ShardingOptions()));

        var shard = await resolver.ResolveByHostAsync(TestShards.Demo01Host);

        Assert.Equal(DatabaseProviderType.SqlServer, shard?.DatabaseProvider);
    }

    [Fact]
    public async Task Explicit_sql_server_metadata_resolves_to_sql_server()
    {
        await _database.SeedAsync();
        await using (var catalog = _database.CreateCatalogContext())
        {
            (await catalog.Tenants.SingleAsync(t => t.TenantCode == "DEMO01")).DatabaseProvider = DatabaseProviderType.SqlServer;
            await catalog.SaveChangesAsync();
        }

        using var catalogRead = _database.CreateCatalogContext();
        var resolver = new TenantShardResolver(catalogRead, new MemoryCache(new MemoryCacheOptions()), Options.Create(new ShardingOptions()));

        Assert.Equal(DatabaseProviderType.SqlServer, (await resolver.ResolveByHostAsync(TestShards.Demo01Host))?.DatabaseProvider);
    }

    [Fact]
    public async Task MySql_metadata_survives_catalog_resolution_and_shard_context()
    {
        await _database.SeedAsync();
        await using (var catalog = _database.CreateCatalogContext())
        {
            var tenant = await catalog.Tenants.SingleAsync(t => t.TenantCode == "DEMO01");
            tenant.DatabaseProvider = DatabaseProviderType.MySql;
            await catalog.SaveChangesAsync();
        }

        using var read = _database.CreateCatalogContext();
        var resolver = new TenantShardResolver(read, new MemoryCache(new MemoryCacheOptions()), Options.Create(new ShardingOptions()));
        var shard = await resolver.ResolveByHostAsync(TestShards.Demo01Host);
        var context = new ShardContext();

        context.Use(shard!);

        Assert.Equal(DatabaseProviderType.MySql, context.Current?.DatabaseProvider);
    }

    [Fact]
    public void MySql_tenant_metadata_selects_mysql_provider_and_lock_adapter()
    {
        using var provider = BuildSqlServerProvider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IShardContext>().Use(
            new ShardDescriptor(Guid.NewGuid(), "MYSQL", "mysql.localhost", "mysql", TenantStatus.Active, DatabaseProviderType.MySql));

        var db = scope.ServiceProvider.GetRequiredService<HrmsDbContext>();
        var lockAdapter = scope.ServiceProvider.GetRequiredService<IEmployeeSerializationLock>();

        Assert.Equal("MySql.EntityFrameworkCore", db.Database.ProviderName);
        Assert.IsType<MySqlLeaveRequestSubmissionLock>(lockAdapter);
    }

    [Fact]
    public async Task Provider_metadata_is_scoped_to_the_resolved_tenant()
    {
        await _database.SeedAsync();
        await using (var catalog = _database.CreateCatalogContext())
        {
            (await catalog.Tenants.SingleAsync(t => t.TenantCode == "DEMO01")).DatabaseProvider = DatabaseProviderType.MySql;
            await catalog.SaveChangesAsync();
        }

        using var read = _database.CreateCatalogContext();
        var resolver = new TenantShardResolver(read, new MemoryCache(new MemoryCacheOptions()), Options.Create(new ShardingOptions()));

        var tenantA = await resolver.ResolveByHostAsync(TestShards.Demo01Host);
        var tenantB = await resolver.ResolveByHostAsync(TestShards.Demo02Host);

        Assert.Equal(DatabaseProviderType.MySql, tenantA?.DatabaseProvider);
        Assert.Equal(DatabaseProviderType.SqlServer, tenantB?.DatabaseProvider);
    }

    [Fact]
    public async Task Unknown_persisted_provider_value_fails_safely()
    {
        await _database.SeedAsync();
        await using (var catalog = _database.CreateCatalogContext())
        {
            await catalog.Database.ExecuteSqlRawAsync(
                "UPDATE Tenants SET DatabaseProvider = 'FutureProvider' WHERE TenantCode = 'DEMO01'");
        }

        using var read = _database.CreateCatalogContext();
        var resolver = new TenantShardResolver(read, new MemoryCache(new MemoryCacheOptions()), Options.Create(new ShardingOptions()));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => resolver.ResolveByHostAsync(TestShards.Demo01Host));
    }

    [Fact]
    public void A_request_cannot_replace_provider_metadata_in_an_existing_shard_context()
    {
        var context = new ShardContext();
        var tenantId = Guid.NewGuid();
        context.Use(new ShardDescriptor(tenantId, "DEMO", "demo.localhost", "demo", TenantStatus.Active, DatabaseProviderType.SqlServer));

        Assert.Throws<InvalidOperationException>(() => context.Use(
            new ShardDescriptor(tenantId, "DEMO", "demo.localhost", "demo", TenantStatus.Active, DatabaseProviderType.MySql)));
        Assert.Equal(DatabaseProviderType.SqlServer, context.Current?.DatabaseProvider);
    }

    private static ServiceProvider BuildSqlServerProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:SqlServer"] = "Server=not-opened;Database=not-opened;",
                ["ConnectionStrings:Catalog"] = "Server=not-opened;Database=not-opened;",
                ["Sharding:MySqlConnectionStringTemplate"] = "Server=not-opened;Database=mysql-routing-{shardKey};"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddScoped<ITenantContext>(_ => new TestTenantContext());
        services.AddInfrastructure(configuration);
        return services.BuildServiceProvider();
    }

    public void Dispose() => _database.Dispose();
}
