using HRMS.Application.Abstractions;
using HRMS.Infrastructure.Persistence;
using HRMS.Infrastructure.Persistence.Catalog;
using HRMS.Infrastructure.Security;
using HRMS.Infrastructure.Sharding;
using HRMS.Tests.TestSupport;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HRMS.Tests;

/// <summary>
/// The development fallback builds its schema with EnsureCreated, which does nothing at all when the
/// database file already exists. A database left behind by an earlier version of the model is therefore
/// missing whatever tables and columns have been added since, and only fails when something first reads
/// or writes one of them. These tests pin the repair.
/// </summary>
public class DatabaseInitializerTests : IDisposable
{
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"hrms-initializer-{Guid.NewGuid():N}.db");

    private readonly string _catalogDatabasePath =
        Path.Combine(Path.GetTempPath(), $"hrms-initializer-catalog-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task Skips_initialization_when_the_development_switch_is_enabled()
    {
        using var loggerFactory = LoggerFactory.Create(_ => { });
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(loggerFactory);

        // No DbContexts or provisioning services are registered intentionally. If the initializer path ran,
        // resolving its first dependency would fail; the explicit Development switch must return before it.
        await DatabaseInitializer.InitializeIfEnabledAsync(
            services.BuildServiceProvider(),
            isDevelopment: true,
            skipInitialization: true);
    }

    [Fact]
    public async Task Rebuilds_a_development_database_that_is_missing_a_table()
    {
        // A database as an earlier phase left it: complete except for a table the model has since gained.
        await InitializeAsync();
        await ExecuteAsync("DROP TABLE RefreshTokens");
        Assert.DoesNotContain("RefreshTokens", await TableNamesAsync());

        await InitializeAsync();

        Assert.Contains("RefreshTokens", await TableNamesAsync());

        // Rebuilding is only worth anything if the database is usable afterwards, seed data included.
        await using var provider = BuildProvider();
        var db = provider.GetRequiredService<HrmsDbContext>();
        Assert.Equal(2, await db.Tenants.CountAsync());
        Assert.True(await db.Users.IgnoreQueryFilters().AnyAsync());
    }

    /// <summary>
    /// The case a table-name comparison cannot see. Adding a property to an entity that already has a table
    /// leaves the table in place and the column absent, which surfaces as "no such column" from a query that
    /// used to work — so staleness is judged per column, not per table.
    /// </summary>
    [Fact]
    public async Task Rebuilds_a_development_database_that_is_missing_a_column()
    {
        await InitializeAsync();
        await ExecuteAsync("ALTER TABLE Tenants DROP COLUMN Address");

        // Asserting the broken state through a real query rather than through the schema: this exception is
        // exactly what a developer would hit on a database left over from an earlier model.
        await Assert.ThrowsAsync<SqliteException>(TenantAddressesAsync);

        await InitializeAsync();

        Assert.Equal(2, (await TenantAddressesAsync()).Count);
    }

    [Fact]
    public async Task Leaves_a_current_database_alone()
    {
        await InitializeAsync();

        await using (var seeded = BuildProvider())
        {
            var db = seeded.GetRequiredService<HrmsDbContext>();
            db.Tenants.First(t => t.TenantCode == "DEMO01").TenantName = "Renamed By Hand";
            await db.SaveChangesAsync();
        }

        // The schema matches the model, so nothing is dropped and hand-made changes survive a restart.
        await InitializeAsync();

        await using var provider = BuildProvider();
        var reread = provider.GetRequiredService<HrmsDbContext>();
        Assert.Equal("Renamed By Hand", (await reread.Tenants.FirstAsync(t => t.TenantCode == "DEMO01")).TenantName);
    }

    /// <summary>
    /// The catalog is a separate database, and the routing rows are the reason it exists: if initialization
    /// left it empty, every host would resolve to nothing and no one could sign in anywhere.
    /// </summary>
    [Fact]
    public async Task Seeds_the_catalog_with_the_hosts_that_route_to_each_tenant()
    {
        await InitializeAsync();

        await using var provider = BuildProvider();
        var catalog = provider.GetRequiredService<HrmsCatalogDbContext>();

        var demo01 = await catalog.Tenants.SingleAsync(t => t.TenantCode == "DEMO01");
        Assert.Equal("demo01.localhost", demo01.Host);
        Assert.Equal("demo01", demo01.ShardKey);

        Assert.True(await catalog.TenantBranding.AnyAsync(b => b.TenantId == demo01.Id));
    }

    /// <summary>
    /// The catalog holds the two tables that have to be readable before a tenant database is chosen, and
    /// nothing else. Getting this wrong fails open — the catalog quietly grows the tenant tables through
    /// navigation discovery rather than throwing — so it is asserted rather than assumed.
    /// </summary>
    [Fact]
    public async Task The_catalog_model_maps_only_the_routing_tables()
    {
        await using var provider = BuildProvider();
        var catalog = provider.GetRequiredService<HrmsCatalogDbContext>();

        var tables = catalog.Model.GetEntityTypes()
            .Select(entityType => entityType.GetTableName())
            .OrderBy(table => table, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(["TenantBranding", "Tenants"], tables);
    }

    /// <summary>
    /// The mirror of the assertion above. A shard database has no TenantBranding table, so a stray branding
    /// configuration reaching the tenant model would make the initializer see a missing table and drop and
    /// recreate every tenant's database on every startup.
    /// </summary>
    [Fact]
    public async Task The_tenant_model_does_not_map_branding()
    {
        await using var provider = BuildProvider();
        var db = provider.GetRequiredService<HrmsDbContext>();

        Assert.DoesNotContain(
            "TenantBranding",
            db.Model.GetEntityTypes().Select(entityType => entityType.GetTableName()));
    }

    private async Task InitializeAsync()
    {
        await using var provider = BuildProvider();
        await DatabaseInitializer.InitializeAsync(provider);
    }

    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IPasswordHasher, IdentityPasswordHasher>();

        // Startup runs before any request, so no tenant is resolved — as in the real host.
        services.AddScoped<ITenantContext>(_ => new TestTenantContext());

        // The initializer provisions each organization in its own scope through these two. The scope is what
        // carries the shard selection, so both are registered exactly as Infrastructure registers them —
        // a stand-in here would test a different code path from the one that runs.
        services.AddScoped<IShardContext, ShardContext>();
        services.AddSingleton<ITenantProvisioningService, TenantProvisioningService>();

        // One connection string for every shard, which is the shared-database mode the application ships
        // with: both demo organizations are seeded into this one file, one pass each.
        services.AddDbContext<HrmsDbContext>(options => options.UseSqlite($"Data Source={_databasePath}"));
        services.AddDbContext<HrmsCatalogDbContext>(
            options => options.UseSqlite($"Data Source={_catalogDatabasePath}"));

        return services.BuildServiceProvider();
    }

    private async Task<List<string>> TableNamesAsync()
    {
        await using var provider = BuildProvider();
        var db = provider.GetRequiredService<HrmsDbContext>();
        return await db.Database
            .SqlQuery<string>($"SELECT name AS \"Value\" FROM sqlite_master WHERE type = 'table'")
            .ToListAsync();
    }

    private async Task<List<string?>> TenantAddressesAsync()
    {
        await using var provider = BuildProvider();
        var db = provider.GetRequiredService<HrmsDbContext>();
        return await db.Tenants.Select(tenant => tenant.Address).ToListAsync();
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var provider = BuildProvider();
        await provider.GetRequiredService<HrmsDbContext>().Database.ExecuteSqlRawAsync(sql);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        DeleteIfPossible(_databasePath);
        DeleteIfPossible(_catalogDatabasePath);
    }

    private static void DeleteIfPossible(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
