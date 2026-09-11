using HRMS.Application.Abstractions;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure;
using HRMS.Infrastructure.Persistence;
using HRMS.Infrastructure.Persistence.Catalog;
using HRMS.Infrastructure.Sharding;
using HRMS.Tests.TestSupport;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Sdk;

namespace HRMS.Tests;

public sealed class ProviderCombinationRoutingTests : IAsyncLifetime
{
    private SqlServerAcceptanceRun? _sqlServerRun;

    public async Task InitializeAsync()
    {
        _sqlServerRun = SqlServerAcceptanceRun.FromEnvironment();
        if (_sqlServerRun is null)
        {
            return;
        }

        await _sqlServerRun.CreateDatabasesAsync();
        await _sqlServerRun.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_sqlServerRun is not null)
        {
            SqlConnection.ClearAllPools();
            await _sqlServerRun.DropDatabasesAsync();
        }
    }

    [SqlServerRequiredFact]
    public Task SqlServer_catalog_routes_to_SqlServer_tenant() =>
        AssertCombinationAsync(
            CatalogProvider.SqlServer,
            DatabaseProviderType.SqlServer);

    [SqlServerRequiredFact]
    public Task SqlServer_catalog_routes_to_MySql_tenant() =>
        AssertCombinationAsync(
            CatalogProvider.SqlServer,
            DatabaseProviderType.MySql);

    [Fact]
    public Task MySql_catalog_routes_to_SqlServer_tenant() =>
        AssertCombinationAsync(
            CatalogProvider.MySql,
            DatabaseProviderType.SqlServer);

    [Fact]
    public Task MySql_catalog_routes_to_MySql_tenant() =>
        AssertCombinationAsync(
            CatalogProvider.MySql,
            DatabaseProviderType.MySql);

    private async Task AssertCombinationAsync(
        CatalogProvider catalogProvider,
        DatabaseProviderType tenantProvider)
    {
        var catalogConnection = await CatalogConnectionAsync(catalogProvider);
        var tenantId = Guid.NewGuid();
        var tenant = new Tenant
        {
            Id = tenantId,
            TenantCode = ($"M{tenantId:N}")[..20],
            Host = $"{tenantId:N}.provider-matrix.test",
            ShardKey = $"provider-matrix-{tenantId:N}",
            DatabaseProvider = tenantProvider,
            TenantName = $"Provider matrix {tenantProvider}",
            Status = TenantStatus.Active
        };

        using (var services = BuildProvider(catalogProvider, catalogConnection, tenantId))
        {
            await using (var scope = services.CreateAsyncScope())
            {
                var catalog = scope.ServiceProvider.GetRequiredService<HrmsCatalogDbContext>();

                try
                {
                    Assert.Equal(ExpectedProviderName(catalogProvider), catalog.Database.ProviderName);

                    catalog.Tenants.Add(tenant);
                    await catalog.SaveChangesAsync();

                    var resolver = scope.ServiceProvider.GetRequiredService<ITenantShardResolver>();
                    var descriptor = await resolver.ResolveByHostAsync(tenant.Host);

                    Assert.NotNull(descriptor);
                    Assert.Equal(tenantId, descriptor!.TenantId);
                    Assert.Equal(tenantProvider, descriptor.DatabaseProvider);

                    var shardContext = scope.ServiceProvider.GetRequiredService<IShardContext>();
                    shardContext.Use(descriptor);

                    var tenantContext = scope.ServiceProvider.GetRequiredService<HrmsDbContext>();
                    Assert.Equal(ExpectedProviderName(tenantProvider), tenantContext.Database.ProviderName);
                    var tenantConnection = tenantContext.Database.GetConnectionString();
                    Assert.Contains(tenantProvider is DatabaseProviderType.MySql ? "mysql-provider-matrix" : "sqlserver-provider-matrix", tenantConnection);
                }
                finally
                {
                    catalog.ChangeTracker.Clear();
                    var created = await catalog.Tenants
                        .SingleOrDefaultAsync(value => value.Id == tenantId);
                    if (created is not null)
                    {
                        catalog.Tenants.Remove(created);
                        await catalog.SaveChangesAsync();
                    }
                }
            }
        }
    }

    private async Task<string> CatalogConnectionAsync(CatalogProvider provider)
    {
        if (provider is CatalogProvider.SqlServer)
        {
            if (_sqlServerRun is null)
            {
                throw SkipException.ForSkip(
                    "Provider matrix SQL Server catalog tests not executed: HRMS_SQLSERVER_TEST_SERVER is absent.");
            }

            return _sqlServerRun.Connection(_sqlServerRun.CatalogDatabaseName).ConnectionString;
        }

        var connection = Environment.GetEnvironmentVariable("HRMS_MYSQL_CATALOG_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
        {
            throw SkipException.ForSkip(
                "Provider matrix MySQL catalog tests not executed: HRMS_MYSQL_CATALOG_TEST_CONNECTION is absent.");
        }

        return MySqlApiFactory.NormalizeConnectionString(connection);
    }

    private static ServiceProvider BuildProvider(
        CatalogProvider catalogProvider,
        string catalogConnection,
        Guid tenantId)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:CatalogProvider"] = catalogProvider.ToString(),
                ["ConnectionStrings:Catalog"] = catalogConnection,
                ["ConnectionStrings:SqlServer"] = "Server=not-opened;Database=not-opened;",
                ["Sharding:SqlServerConnectionStringTemplate"] =
                    "Server=not-opened;Database=sqlserver-provider-matrix-{shardKey};",
                ["Sharding:MySqlConnectionStringTemplate"] =
                    "Server=not-opened;Database=mysql-provider-matrix-{shardKey};"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddScoped<ITenantContext>(_ => new TestTenantContext(tenantId));
        services.AddInfrastructure(configuration);
        return services.BuildServiceProvider();
    }

    private static string ExpectedProviderName(CatalogProvider provider) =>
        provider is CatalogProvider.MySql
            ? "MySql.EntityFrameworkCore"
            : "Microsoft.EntityFrameworkCore.SqlServer";

    private static string ExpectedProviderName(DatabaseProviderType provider) =>
        provider is DatabaseProviderType.MySql
            ? "MySql.EntityFrameworkCore"
            : "Microsoft.EntityFrameworkCore.SqlServer";

    private enum CatalogProvider
    {
        SqlServer,
        MySql
    }
}

/// <summary>Marks provider-matrix tests that require the optional SQL Server acceptance instance.</summary>
public sealed class SqlServerRequiredFactAttribute : FactAttribute
{
    public SqlServerRequiredFactAttribute()
    {
        if (!SqlServerAcceptanceRun.IsConfigured)
        {
            Skip = $"Provider matrix SQL Server catalog tests not executed: {SqlServerAcceptanceRun.ServerEnvironmentVariable} is absent.";
        }
    }
}
