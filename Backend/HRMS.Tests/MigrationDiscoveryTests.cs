using HRMS.Infrastructure.Persistence;
using HRMS.Infrastructure.Persistence.Catalog;
using HRMS.Tests.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class MigrationDiscoveryTests
{
    [Fact]
    public void Migration_host_finds_API_settings_when_started_from_its_project_directory()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "HRMS.slnx")))
            current = current.Parent;
        Assert.NotNull(current);
        var repositoryRoot = current!.FullName;
        var migrationHostDirectory = Path.GetFullPath(Path.Combine(repositoryRoot, "Backend", "HRMS.MigrationHost"));
        var expected = Path.GetFullPath(Path.Combine(repositoryRoot, "Backend", "HRMS.API", "appsettings.json"));

        Assert.Equal(expected, global::MigrationHost.ResolveRepositorySettingsPath(
            "appsettings.json", [migrationHostDirectory]));

        var configuration = global::MigrationHost.BuildConfiguration([]);
        var root = (IConfigurationRoot)configuration;
        var providerSummary = string.Join(", ", root.Providers.Select(provider =>
            $"{provider.GetType().Name}:{(provider.TryGet("MigrationHost:AllowedCatalogs:0", out _) ? "has-allowlist" : "no-allowlist")}"));
        Assert.True(configuration["MigrationHost:AllowedCatalogs:0"] == "hrms_catalog", providerSummary);
    }

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
                "20260908161315_AddPasswordRecoverySettings",
                "20261008130000_AddPlatformPasswordRecovery"
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
                "20260908163001_AddPasswordRecoverySettings",
                "20261008130000_AddPlatformPasswordRecovery"
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
        Assert.Contains("20260913121202_AddEmployeeNoticePeriod", migrations);
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
        Assert.Contains("20260912090001_AddAttendanceWorkflowConcurrencyVersion", migrations);
        Assert.Contains("20260913130001_AddEmployeeNoticePeriod", migrations);
        Assert.Contains("20260914152734_AddEmploymentHistoryRevisions", migrations);
        Assert.Contains("20260915100000_EnhanceUserRoleAssignments", migrations);
        Assert.Contains("20261028043743_AddEmployeeImportPersistence", migrations);
        Assert.Contains("20261028052420_AddEmployeeImportClaimLease", migrations);
        Assert.Equal(95, migrations.Length);
        Assert.Equal(migrations.Length, migrations.Distinct(StringComparer.Ordinal).Count());
        Assert.True(
            Array.IndexOf(migrations, "20260907182803_AddUserInvitations")
                < Array.IndexOf(migrations, "20260908163000_AddPasswordResetOtps"));
        Assert.True(
            Array.IndexOf(migrations, "20261028043743_AddEmployeeImportPersistence")
                < Array.IndexOf(migrations, "20261028052420_AddEmployeeImportClaimLease"));
        Assert.Contains("20260913130001_AddEmployeeNoticePeriod", migrations);
    }
}
