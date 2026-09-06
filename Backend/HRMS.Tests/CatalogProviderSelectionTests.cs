using HRMS.Application.Abstractions;
using HRMS.Domain.Entities;
using HRMS.Infrastructure;
using HRMS.Infrastructure.Persistence;
using HRMS.Infrastructure.Persistence.Catalog;
using HRMS.Infrastructure.Sharding;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRMS.Tests;

public sealed class CatalogProviderSelectionTests
{
    [Fact]
    public async Task WebApplicationFactory_legacy_sqlite_provider_selects_isolated_sqlite_catalog()
    {
        using var factory = new HrmsApiFactory();
        using var scope = factory.Services.CreateScope();

        var catalog = scope.ServiceProvider.GetRequiredService<HrmsCatalogDbContext>();
        var resolver = scope.ServiceProvider.GetRequiredService<ITenantShardResolver>();
        var shard = await resolver.ResolveByHostAsync(new Uri(HrmsApiFactory.Demo02Host).Host);

        Assert.Equal("Microsoft.EntityFrameworkCore.Sqlite", catalog.Database.ProviderName);
        Assert.Equal(HRMS.Domain.Enums.TenantStatus.Active, shard?.Status);
    }

    [Fact]
    public void Explicit_sql_server_selects_sql_server_catalog()
    {
        using var provider = BuildProvider("SqlServer");

        using var scope = provider.CreateScope();
        Assert.Equal("Microsoft.EntityFrameworkCore.SqlServer", scope.ServiceProvider
            .GetRequiredService<HrmsCatalogDbContext>().Database.ProviderName);
    }

    [Fact]
    public void Explicit_mysql_selects_oracle_mysql_catalog_provider()
    {
        using var provider = BuildProvider("MySql");

        using var scope = provider.CreateScope();
        Assert.Equal("MySql.EntityFrameworkCore", scope.ServiceProvider
            .GetRequiredService<HrmsCatalogDbContext>().Database.ProviderName);
    }

    [Fact]
    public void Explicit_sqlite_preserves_sqlite_catalog_behavior()
    {
        using var provider = BuildProvider("Sqlite");

        using var scope = provider.CreateScope();
        Assert.Equal("Microsoft.EntityFrameworkCore.Sqlite", scope.ServiceProvider
            .GetRequiredService<HrmsCatalogDbContext>().Database.ProviderName);
    }

    [Fact]
    public void Invalid_explicit_catalog_provider_fails_closed()
    {
        var configuration = Configuration(catalogProvider: "Postgres");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.Throws<InvalidOperationException>(
            () => scope.ServiceProvider.GetRequiredService<HrmsCatalogDbContext>());
    }

    [Fact]
    public void Absent_catalog_provider_preserves_legacy_sqlite_override()
    {
        using var provider = BuildProvider(catalogProvider: null, legacyProvider: "Sqlite");

        using var scope = provider.CreateScope();
        Assert.Equal("Microsoft.EntityFrameworkCore.Sqlite", scope.ServiceProvider
            .GetRequiredService<HrmsCatalogDbContext>().Database.ProviderName);
    }

    [Fact]
    public void Absent_catalog_provider_defaults_to_sql_server()
    {
        using var provider = BuildProvider(catalogProvider: null, legacyProvider: null);

        using var scope = provider.CreateScope();
        Assert.Equal("Microsoft.EntityFrameworkCore.SqlServer", scope.ServiceProvider
            .GetRequiredService<HrmsCatalogDbContext>().Database.ProviderName);
    }

    [Theory]
    [InlineData("SqlServer", "MySql")]
    [InlineData("MySql", "SqlServer")]
    public void Tenant_provider_metadata_does_not_select_catalog_provider(string catalogProvider, string tenantProvider)
    {
        using var provider = BuildProvider(catalogProvider, tenantProvider);

        using var scope = provider.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<HrmsCatalogDbContext>();

        var expectedCatalogProvider = catalogProvider == "MySql"
            ? "MySql.EntityFrameworkCore"
            : "Microsoft.EntityFrameworkCore.SqlServer";

        Assert.Equal(expectedCatalogProvider, catalog.Database.ProviderName);
    }

    [Fact]
    public void Database_provider_is_mapped_in_catalog_and_ignored_in_tenant_model()
    {
        using var provider = BuildProvider("Sqlite");
        using var scope = provider.CreateScope();

        var catalog = scope.ServiceProvider.GetRequiredService<HrmsCatalogDbContext>();
        Assert.NotNull(catalog.Model.FindEntityType(typeof(Tenant))!
            .FindProperty(nameof(Tenant.DatabaseProvider)));

        var tenantOptions = new DbContextOptionsBuilder<HrmsDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        using var tenant = new HrmsDbContext(tenantOptions, new TestTenantContext());

        Assert.Null(tenant.Model.FindEntityType(typeof(Tenant))?
            .FindProperty(nameof(Tenant.DatabaseProvider)));
    }

    private static ServiceProvider BuildProvider(string? catalogProvider, string? tenantProvider = null, string? legacyProvider = null)
    {
        var configuration = Configuration(catalogProvider, legacyProvider, tenantProvider);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<ITenantContext>(_ => new TestTenantContext());
        services.AddInfrastructure(configuration);
        return services.BuildServiceProvider();
    }

    private static IConfiguration Configuration(
        string? catalogProvider,
        string? legacyProvider = null,
        string? tenantProvider = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Catalog"] = "Server=not-opened;Database=not-opened;",
            ["ConnectionStrings:SqlServer"] = "Server=not-opened;Database=not-opened;",
            ["ConnectionStrings:SqliteCatalog"] = "Data Source=:memory:"
        };

        if (catalogProvider is not null)
        {
            values["Database:CatalogProvider"] = catalogProvider;
        }

        if (legacyProvider is not null)
        {
            values["Database:Provider"] = legacyProvider;
        }

        if (tenantProvider is not null)
        {
            values["Test:TenantDatabaseProvider"] = tenantProvider;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}
