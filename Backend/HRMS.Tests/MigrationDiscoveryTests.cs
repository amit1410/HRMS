using HRMS.Infrastructure.Persistence;
using HRMS.Infrastructure.Persistence.Catalog;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class MigrationDiscoveryTests
{
    [Fact]
    public void SqlServer_catalog_chain_contains_recovery_migrations_in_order()
    {
        using var db = new HrmsCatalogDbContext(new DbContextOptionsBuilder<HrmsCatalogDbContext>()
            .UseSqlServer("Server=not-opened;Database=not-opened;", sql =>
                sql.MigrationsAssembly(typeof(HrmsCatalogDbContext).Assembly.FullName))
            .Options);

        Assert.Equal(
            [
                "20260823113202_InitialCatalog",
                "20260905142921_AddTenantDatabaseProvider",
                "20260906130913_AddPlatformIdentity",
                "20260908151928_AddTenantLoginIdentifierMode",
                "20260908161315_AddPasswordRecoverySettings"
            ],
            db.Database.GetMigrations().ToArray());
    }

    [Fact]
    public void MySql_catalog_chain_contains_recovery_migrations_in_order()
    {
        using var db = new HrmsCatalogDbContext(new DbContextOptionsBuilder<HrmsCatalogDbContext>()
            .UseMySQL("Server=not-opened;Database=not-opened;", mysql =>
                mysql.MigrationsAssembly("HRMS.Infrastructure.MySqlCatalogMigrations"))
            .Options);

        Assert.Equal(
            [
                "20260905181639_InitialMySqlCatalogSchema",
                "20260906130913_AddPlatformIdentity",
                "20260908151956_AddTenantLoginIdentifierMode",
                "20260908163001_AddPasswordRecoverySettings"
            ],
            db.Database.GetMigrations().ToArray());
    }

    [Fact]
    public void SqlServer_tenant_chain_contains_invitations_before_recovery_table()
    {
        using var db = new HrmsDbContext(new DbContextOptionsBuilder<HrmsDbContext>()
            .UseSqlServer("Server=not-opened;Database=not-opened;", sql =>
                sql.MigrationsAssembly(typeof(HrmsDbContext).Assembly.FullName))
            .Options, new TestTenantContext());

        var migrations = db.Database.GetMigrations().ToArray();
        Assert.Contains("20260907182740_AddUserInvitations", migrations);
        Assert.Contains("20260908161324_AddPasswordResetOtps", migrations);
        Assert.True(
            Array.IndexOf(migrations, "20260907182740_AddUserInvitations")
                < Array.IndexOf(migrations, "20260908161324_AddPasswordResetOtps"));
    }

    [Fact]
    public void MySql_tenant_chain_contains_invitations_before_recovery_table()
    {
        using var db = new HrmsDbContext(new DbContextOptionsBuilder<HrmsDbContext>()
            .UseMySQL("Server=not-opened;Database=not-opened;", mysql =>
                mysql.MigrationsAssembly("HRMS.Infrastructure.MySqlMigrations"))
            .Options, new TestTenantContext());

        var migrations = db.Database.GetMigrations().ToArray();
        Assert.Contains("20260905172008_InitialMySqlTenantSchema", migrations);
        Assert.Contains("20260907182803_AddUserInvitations", migrations);
        Assert.Contains("20260908163000_AddPasswordResetOtps", migrations);
        Assert.True(
            Array.IndexOf(migrations, "20260907182803_AddUserInvitations")
                < Array.IndexOf(migrations, "20260908163000_AddPasswordResetOtps"));
    }
}
