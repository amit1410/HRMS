using HRMS.Application.Abstractions;
using HRMS.Infrastructure.Persistence;
using HRMS.Infrastructure.Persistence.Catalog;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Infrastructure.Security;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests.TestSupport;

/// <summary>
/// A real SQLite database kept entirely in memory for the lifetime of the instance. One open
/// connection is shared by every context created here, so contexts scoped to different tenants all
/// observe the same data — which is exactly what tenant-isolation tests need. Dispose closes the
/// connection and discards the database.
/// <para>
/// Two connections, because there are two databases: the catalog and one tenant database. Keeping them
/// genuinely separate in tests is the point — a query that only works because both tables happen to live
/// in one file would pass here and fail in production, which is the whole class of bug this split
/// introduces.
/// </para>
/// </summary>
public sealed class SqliteInMemoryDatabase : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SqliteConnection _catalogConnection;

    public SqliteInMemoryDatabase()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _catalogConnection = new SqliteConnection("DataSource=:memory:");
        _catalogConnection.Open();

        // Create both schemas from their models once, against their own shared connections.
        using var context = CreateContext(new TestTenantContext());
        context.Database.EnsureCreated();

        using var catalog = CreateCatalogContext();
        catalog.Database.EnsureCreated();
    }

    /// <summary>Creates a context bound to the shared tenant database with the supplied tenant scope.</summary>
    public HrmsDbContext CreateContext(ITenantContext tenantContext)
    {
        var options = new DbContextOptionsBuilder<HrmsDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new HrmsDbContext(options, tenantContext);
    }

    /// <summary>
    /// Creates a context bound to the shared catalog database. No tenant scope, because the catalog is what
    /// resolves the tenant — nothing read through it is filtered.
    /// </summary>
    public HrmsCatalogDbContext CreateCatalogContext()
    {
        var options = new DbContextOptionsBuilder<HrmsCatalogDbContext>()
            .UseSqlite(_catalogConnection)
            .Options;
        return new HrmsCatalogDbContext(options);
    }

    /// <summary>
    /// Runs the production seeders the way startup runs them: the catalog first, then every organization it
    /// holds, one at a time.
    /// <para>
    /// Both organizations land in the one tenant database here, because this harness has one — which is the
    /// shared-database deployment mode, and what lets the tenant-isolation tests prove the query filters do
    /// their job with both organizations' rows genuinely present in the same tables.
    /// </para>
    /// </summary>
    public async Task SeedAsync()
    {
        using var catalog = CreateCatalogContext();
        await DatabaseSeeder.SeedCatalogAsync(catalog, CancellationToken.None);

        var tenants = await catalog.Tenants.AsNoTracking().ToListAsync();

        using var context = CreateContext(new TestTenantContext());
        foreach (var tenant in tenants)
        {
            await DatabaseSeeder.SeedShardAsync(
                context, new IdentityPasswordHasher(), tenant, CancellationToken.None);
        }
    }

    public void Dispose()
    {
        _connection.Dispose();
        _catalogConnection.Dispose();
    }
}
