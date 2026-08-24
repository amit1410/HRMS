using HRMS.Application.Abstractions;
using HRMS.Domain.Authorization;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Infrastructure.Persistence.Catalog;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Infrastructure.Security;
using HRMS.Infrastructure.Sharding;
using HRMS.Tests.TestSupport;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HRMS.Tests;

/// <summary>
/// Provisioning is the one place that opens a database per organization from a single code path, so these
/// tests are written against separate database files rather than the shared one every other test uses. That
/// is the deployment mode where the interesting failures live: a shard that cannot be reached, and an
/// organization whose rows must not end up in a neighbour's file.
/// </summary>
public class TenantProvisioningTests : IDisposable
{
    private readonly string _catalogPath =
        Path.Combine(Path.GetTempPath(), $"hrms-provision-catalog-{Guid.NewGuid():N}.db");

    private readonly Dictionary<string, string> _shardPaths = new(StringComparer.Ordinal)
    {
        ["demo01"] = Path.Combine(Path.GetTempPath(), $"hrms-provision-demo01-{Guid.NewGuid():N}.db"),
        ["demo02"] = Path.Combine(Path.GetTempPath(), $"hrms-provision-demo02-{Guid.NewGuid():N}.db")
    };

    /// <summary>
    /// The point of the whole exercise: one loop, one code path, and each organization's people in its own
    /// database with nothing of anyone else's.
    /// </summary>
    [Fact]
    public async Task Each_organization_is_provisioned_into_its_own_database()
    {
        await InitializeAsync();

        await AssertHoldsOnlyAsync("demo01", SeedData.TenantIds.Demo01, expectedEmployees: 6);
        await AssertHoldsOnlyAsync("demo02", SeedData.TenantIds.Demo02, expectedEmployees: 2);
    }

    /// <summary>
    /// Reference data is not per-organization, so every database has to carry the full set — a shard missing
    /// a permission row would authorize nothing for that grant and only that customer.
    /// </summary>
    [Fact]
    public async Task Every_shard_carries_the_full_reference_data()
    {
        await InitializeAsync();

        foreach (var shardKey in _shardPaths.Keys)
        {
            await using var provider = BuildProvider();
            var db = OpenShard(provider, shardKey);

            Assert.Equal(RoleNames.All.Count, await db.Roles.CountAsync());
            Assert.Equal(Permissions.All.Count, await db.Permissions.CountAsync());
        }
    }

    /// <summary>
    /// Startup runs on every boot, so provisioning has to be safe to repeat. The second pass must add nothing
    /// and drop nothing — including not dropping and recreating the database, which would silently delete a
    /// customer's data on restart.
    /// </summary>
    [Fact]
    public async Task Provisioning_twice_changes_nothing()
    {
        await InitializeAsync();

        await using (var seeded = BuildProvider())
        {
            var db = OpenShard(seeded, "demo01");
            db.Tenants.Single().TenantName = "Renamed By Hand";
            await db.SaveChangesAsync();
        }

        await InitializeAsync();

        await using var provider = BuildProvider();
        var reread = OpenShard(provider, "demo01");

        Assert.Equal("Renamed By Hand", (await reread.Tenants.SingleAsync()).TenantName);
        Assert.Equal(6, await reread.Employees.IgnoreQueryFilters().CountAsync());
        Assert.Equal(RoleNames.All.Count, await reread.Roles.CountAsync());
    }

    /// <summary>
    /// The asymmetry the initializer is built around: one customer's unreachable database must not take the
    /// others offline with it.
    /// </summary>
    [Fact]
    public async Task An_unreachable_shard_is_skipped_and_the_others_are_provisioned()
    {
        // A directory that does not exist, so SQLite cannot create the file — the closest local stand-in for
        // a tenant database whose server is down.
        _shardPaths["demo02"] = Path.Combine(Path.GetTempPath(), $"hrms-no-such-dir-{Guid.NewGuid():N}", "x.db");

        await InitializeAsync();

        await AssertHoldsOnlyAsync("demo01", SeedData.TenantIds.Demo01, expectedEmployees: 6);
        Assert.False(File.Exists(_shardPaths["demo02"]));
    }

    /// <summary>
    /// The catalog row is what decides which database an organization's data belongs in, so provisioning
    /// without one has nowhere correct to write. It throws rather than guessing: writing the rows to whatever
    /// the descriptor happened to name would put a customer's data in an unrecorded database that nothing
    /// would ever route to again.
    /// </summary>
    [Fact]
    public async Task Provisioning_an_organization_the_catalog_does_not_hold_throws()
    {
        await InitializeAsync();

        await using var provider = BuildProvider();
        var provisioning = provider.GetRequiredService<ITenantProvisioningService>();

        var stranger = new ShardDescriptor(
            Guid.NewGuid(), "GHOST", "ghost.localhost", "demo01", TenantStatus.Active);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provisioning.ProvisionAsync(stranger));

        Assert.Contains("no row in the catalog", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A suspended organization is still provisioned. Its schema has to be current for reactivation to be a
    /// status flip rather than a migration exercise; what suspension withholds is service, not maintenance.
    /// </summary>
    [Fact]
    public async Task An_inactive_organization_is_still_provisioned()
    {
        await InitializeAsync();

        await using (var suspending = BuildProvider())
        {
            var catalog = suspending.GetRequiredService<HrmsCatalogDbContext>();
            catalog.Tenants.Single(t => t.ShardKey == "demo02").Status = TenantStatus.Suspended;
            await catalog.SaveChangesAsync();
        }

        // Deleted so the second pass has something to do: if suspension stopped provisioning, this file would
        // simply stay gone. Pools first — SQLite keeps the file handle open after the context is disposed.
        SqliteConnection.ClearAllPools();
        File.Delete(_shardPaths["demo02"]);

        await InitializeAsync();

        await AssertHoldsOnlyAsync("demo02", SeedData.TenantIds.Demo02, expectedEmployees: 2);
    }

    private async Task AssertHoldsOnlyAsync(string shardKey, Guid tenantId, int expectedEmployees)
    {
        await using var provider = BuildProvider();
        var db = OpenShard(provider, shardKey);

        // IgnoreQueryFilters throughout: the assertion is about what the file contains, and a filter that
        // hid a neighbour's rows would make a leak look like isolation.
        var tenant = Assert.Single(await db.Tenants.ToListAsync());
        Assert.Equal(tenantId, tenant.Id);

        Assert.Equal(expectedEmployees, await db.Employees.IgnoreQueryFilters().CountAsync());
        Assert.All(
            await db.Employees.IgnoreQueryFilters().ToListAsync(),
            employee => Assert.Equal(tenantId, employee.TenantId));
        Assert.All(
            await db.Users.IgnoreQueryFilters().ToListAsync(),
            user => Assert.Equal(tenantId, user.TenantId));
    }

    /// <summary>
    /// Selects a shard on a fresh scope and returns its context, which is the only way to read one of these
    /// databases — the connection is chosen when the context is first resolved in a scope.
    /// </summary>
    private static HrmsDbContext OpenShard(IServiceProvider provider, string shardKey)
    {
        var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IShardContext>().Use(new ShardDescriptor(
            Guid.Empty, shardKey.ToUpperInvariant(), $"{shardKey}.localhost", shardKey, TenantStatus.Active));

        return scope.ServiceProvider.GetRequiredService<HrmsDbContext>();
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
        services.AddScoped<ITenantContext>(_ => new TestTenantContext());
        services.AddScoped<IShardContext, ShardContext>();
        services.AddSingleton<ITenantProvisioningService, TenantProvisioningService>();

        // The registration under test in miniature: the connection string is read from the scope's shard,
        // exactly as Infrastructure does it, so each organization opens its own file. A scope with no shard
        // has no database to open, which is the production behaviour too.
        services.AddDbContext<HrmsDbContext>((serviceProvider, options) =>
        {
            var shard = serviceProvider.GetRequiredService<IShardContext>().Current
                ?? throw new InvalidOperationException("No shard was selected for this scope.");

            options.UseSqlite($"Data Source={_shardPaths[shard.ShardKey]}");
        });

        services.AddDbContext<HrmsCatalogDbContext>(options => options.UseSqlite($"Data Source={_catalogPath}"));

        return services.BuildServiceProvider();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        DeleteIfPossible(_catalogPath);
        foreach (var path in _shardPaths.Values)
        {
            DeleteIfPossible(path);
        }
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
