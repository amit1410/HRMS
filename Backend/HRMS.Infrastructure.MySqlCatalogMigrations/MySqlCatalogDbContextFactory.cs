using HRMS.Infrastructure.Persistence.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HRMS.Infrastructure.MySqlCatalogMigrations;

public sealed class MySqlCatalogDbContextFactory : IDesignTimeDbContextFactory<HrmsCatalogDbContext>
{
    public HrmsCatalogDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__MySqlCatalog");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException("ConnectionStrings__MySqlCatalog is required for MySQL catalog EF design-time tooling.");
        var options = new DbContextOptionsBuilder<HrmsCatalogDbContext>()
            .UseMySQL(
                connection,
                mysql => mysql.MigrationsAssembly(typeof(MySqlCatalogDbContextFactory).Assembly.FullName))
            .Options;

        return new HrmsCatalogDbContext(options);
    }
}
