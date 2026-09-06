using HRMS.Infrastructure.Persistence.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HRMS.Infrastructure.MySqlCatalogMigrations;

public sealed class MySqlCatalogDbContextFactory : IDesignTimeDbContextFactory<HrmsCatalogDbContext>
{
    public HrmsCatalogDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<HrmsCatalogDbContext>()
            .UseMySQL(
                "Server=localhost;Database=HRMS_Catalog_MigrationDesign;",
                mysql => mysql.MigrationsAssembly(typeof(MySqlCatalogDbContextFactory).Assembly.FullName))
            .Options;

        return new HrmsCatalogDbContext(options);
    }
}
