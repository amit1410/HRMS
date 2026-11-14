using HRMS.Infrastructure.Persistence;
using HRMS.Infrastructure.Persistence.Catalog;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Text.RegularExpressions;

namespace HRMS.Tests;

public sealed class MySqlMigrationIdentifierTests
{
    private const int MySqlMaximumIdentifierLength = 64;
    private const string MySqlCatalogMigrationsAssembly = "HRMS.Infrastructure.MySqlCatalogMigrations";
    private const string MySqlTenantMigrationsAssembly = "HRMS.Infrastructure.MySqlMigrations";

    [Fact]
    public void Catalog_migration_sql_contains_no_identifier_over_mysql_limit()
    {
        using var db = new HrmsCatalogDbContext(new DbContextOptionsBuilder<HrmsCatalogDbContext>()
            .UseMySQL("Server=not-opened;Database=not-opened;", mysql =>
                mysql.MigrationsAssembly(MySqlCatalogMigrationsAssembly))
            .Options);

        var sql = db.GetService<IMigrator>().GenerateScript();

        AssertIdentifierLengths(sql, MySqlCatalogMigrationsAssembly);
        Assert.Contains("IX_PasswordResetTokens_User_Used_Expires", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("IX_PlatformPasswordResetTokens_PlatformUserId_UsedAtUtc_ExpiresAtUtc", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Tenant_migration_sql_contains_no_identifier_over_mysql_limit()
    {
        using var db = new HrmsDbContext(new DbContextOptionsBuilder<HrmsDbContext>()
            .UseMySQL("Server=not-opened;Database=not-opened;", mysql =>
                mysql.MigrationsAssembly(MySqlTenantMigrationsAssembly))
            .Options, new TestTenantContext());

        var sql = db.GetService<IMigrator>().GenerateScript();

        AssertIdentifierLengths(sql, MySqlTenantMigrationsAssembly);
    }

    [Fact]
    public void MySql_catalog_and_tenant_migration_sequences_are_discoverable_for_fresh_and_migration_down_databases()
    {
        using var catalog = new HrmsCatalogDbContext(new DbContextOptionsBuilder<HrmsCatalogDbContext>()
            .UseMySQL("Server=not-opened;Database=catalog-migration-down;", mysql =>
                mysql.MigrationsAssembly(MySqlCatalogMigrationsAssembly))
            .Options);
        using var tenant = new HrmsDbContext(new DbContextOptionsBuilder<HrmsDbContext>()
            .UseMySQL("Server=not-opened;Database=tenant-migration-down;", mysql =>
                mysql.MigrationsAssembly(MySqlTenantMigrationsAssembly))
            .Options, new TestTenantContext());

        Assert.NotEmpty(catalog.Database.GetMigrations());
        Assert.NotEmpty(tenant.Database.GetMigrations());
        Assert.Contains("PlatformPasswordResetTokens", catalog.GetService<IMigrator>().GenerateScript(), StringComparison.Ordinal);
        Assert.Equal("catalog-migration-down", catalog.Database.GetDbConnection().Database);
        Assert.Equal("tenant-migration-down", tenant.Database.GetDbConnection().Database);
    }

    private static void AssertIdentifierLengths(string sql, string migrationAssembly)
    {
        var identifiers = Regex.Matches(sql, "`(?<identifier>[^`]+)`")
            .Select(match => match.Groups["identifier"].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var excessive = identifiers
            .Where(identifier => identifier.Length > MySqlMaximumIdentifierLength)
            .ToArray();

        Assert.True(excessive.Length == 0,
            $"MySQL migration SQL from {migrationAssembly} contains identifiers over 64 characters: " +
            string.Join(", ", excessive.Select(identifier => $"{identifier} ({identifier.Length})")));
    }
}
