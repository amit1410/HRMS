using HRMS.Infrastructure.Persistence;
using HRMS.Infrastructure.Persistence.Catalog;

namespace HRMS.Tests;

public sealed class DesignTimeProviderFactoryTests
{
    [Fact]
    public void Tenant_factory_selects_each_explicit_provider_without_fallback()
    {
        var originalProvider = Environment.GetEnvironmentVariable("Database__Provider");
        var originalMySql = Environment.GetEnvironmentVariable("ConnectionStrings__MySql");
        try
        {
            Environment.SetEnvironmentVariable("Database__Provider", "MySql");
            Environment.SetEnvironmentVariable("ConnectionStrings__MySql", "Server=localhost;Database=design;User ID=design;Password=design;");
            using (var mysql = new HrmsDbContextFactory().CreateDbContext([]))
                Assert.Equal("MySql.EntityFrameworkCore", mysql.Database.ProviderName);

            Environment.SetEnvironmentVariable("Database__Provider", "SqlServer");
            using (var sqlServer = new HrmsDbContextFactory().CreateDbContext([]))
                Assert.Equal("Microsoft.EntityFrameworkCore.SqlServer", sqlServer.Database.ProviderName);
        }
        finally
        {
            Environment.SetEnvironmentVariable("Database__Provider", originalProvider);
            Environment.SetEnvironmentVariable("ConnectionStrings__MySql", originalMySql);
        }
    }

    [Fact]
    public void Explicit_mysql_without_connection_fails_clearly()
    {
        var originalProvider = Environment.GetEnvironmentVariable("Database__Provider");
        var originalMySql = Environment.GetEnvironmentVariable("ConnectionStrings__MySql");
        try
        {
            Environment.SetEnvironmentVariable("Database__Provider", "MySql");
            Environment.SetEnvironmentVariable("ConnectionStrings__MySql", null);
            var error = Assert.Throws<InvalidOperationException>(() => new HrmsDbContextFactory().CreateDbContext([]));
            Assert.Contains("ConnectionStrings:MySql", error.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("Database__Provider", originalProvider);
            Environment.SetEnvironmentVariable("ConnectionStrings__MySql", originalMySql);
        }
    }

    [Fact]
    public void Catalog_factory_uses_mysql_catalog_migration_assembly_when_requested()
    {
        var originalProvider = Environment.GetEnvironmentVariable("Database__Provider");
        var originalCatalog = Environment.GetEnvironmentVariable("ConnectionStrings__MySqlCatalog");
        try
        {
            Environment.SetEnvironmentVariable("Database__Provider", "MySql");
            Environment.SetEnvironmentVariable("ConnectionStrings__MySqlCatalog", "Server=localhost;Database=catalog;User ID=design;Password=design;");
            using var catalog = new HrmsCatalogDbContextFactory().CreateDbContext([]);
            Assert.Equal("MySql.EntityFrameworkCore", catalog.Database.ProviderName);
        }
        finally
        {
            Environment.SetEnvironmentVariable("Database__Provider", originalProvider);
            Environment.SetEnvironmentVariable("ConnectionStrings__MySqlCatalog", originalCatalog);
        }
    }
}
