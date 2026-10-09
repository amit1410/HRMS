using HRMS.Infrastructure.Persistence.Catalog;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class CatalogOnlyMigrationTests
{
    [Fact]
    public async Task Catalog_only_executor_invokes_only_the_catalog_operation()
    {
        using var catalog = new HrmsCatalogDbContext(new DbContextOptionsBuilder<HrmsCatalogDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options);
        var calls = 0;
        var executor = new CatalogOnlyMigrationExecutor((_, _) =>
        {
            calls++;
            return Task.CompletedTask;
        });

        await executor.ExecuteAsync(new CatalogOnlyMigrationTarget(catalog), CancellationToken.None);

        Assert.Equal(1, calls);
        var targetProperties = typeof(CatalogOnlyMigrationTarget).GetProperties();
        Assert.Single(targetProperties);
        Assert.Equal(nameof(CatalogOnlyMigrationTarget.Catalog), targetProperties[0].Name);
        Assert.DoesNotContain(targetProperties, property => property.Name.Contains("Tenant", StringComparison.OrdinalIgnoreCase));
    }
}
