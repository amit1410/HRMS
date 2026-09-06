using HRMS.Application.Abstractions;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure;
using HRMS.Infrastructure.Persistence.Catalog;
using HRMS.Infrastructure.Sharding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Sdk;

namespace HRMS.Tests;

public sealed class MySqlCatalogRuntimeIntegrationTests
{
    [Fact]
    public async Task MySql_catalog_provider_is_selected_from_trusted_configuration()
    {
        await RunScenarioAsync((_, catalog) =>
        {
            Assert.Equal("MySql.EntityFrameworkCore", catalog.Database.ProviderName);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task MySql_catalog_round_trips_tenants_and_both_database_providers()
    {
        await RunScenarioAsync(async (scenario, catalog) =>
        {
            catalog.Tenants.AddRange(
                scenario.CreateTenant(DatabaseProviderType.SqlServer),
                scenario.CreateTenant(DatabaseProviderType.MySql));

            await catalog.SaveChangesAsync();

            var sqlServerTenant = await catalog.Tenants
                .AsNoTracking()
                .SingleAsync(tenant => tenant.Id == scenario.SqlServerTenantId);
            var mySqlTenant = await catalog.Tenants
                .AsNoTracking()
                .SingleAsync(tenant => tenant.Id == scenario.MySqlTenantId);

            var expectedSqlServerTenant = scenario.CreateTenant(DatabaseProviderType.SqlServer);
            var expectedMySqlTenant = scenario.CreateTenant(DatabaseProviderType.MySql);

            Assert.Equal(scenario.SqlServerTenantId, sqlServerTenant.Id);
            Assert.Equal(expectedSqlServerTenant.TenantCode, sqlServerTenant.TenantCode);
            Assert.Equal(expectedSqlServerTenant.Host, sqlServerTenant.Host);
            Assert.Equal(expectedSqlServerTenant.ShardKey, sqlServerTenant.ShardKey);
            Assert.Equal(DatabaseProviderType.SqlServer, sqlServerTenant.DatabaseProvider);
            Assert.Equal(scenario.SqlServerTenantName, sqlServerTenant.TenantName);
            Assert.Equal(TenantStatus.Active, sqlServerTenant.Status);

            Assert.Equal(scenario.MySqlTenantId, mySqlTenant.Id);
            Assert.Equal(expectedMySqlTenant.TenantCode, mySqlTenant.TenantCode);
            Assert.Equal(expectedMySqlTenant.Host, mySqlTenant.Host);
            Assert.Equal(expectedMySqlTenant.ShardKey, mySqlTenant.ShardKey);
            Assert.Equal(DatabaseProviderType.MySql, mySqlTenant.DatabaseProvider);
            Assert.Equal(scenario.MySqlTenantName, mySqlTenant.TenantName);
            Assert.Equal(TenantStatus.Active, mySqlTenant.Status);
        });
    }

    [Fact]
    public async Task MySql_catalog_resolves_tenant_metadata_through_production_shard_resolver()
    {
        await RunScenarioAsync(async (scenario, catalog) =>
        {
            catalog.Tenants.Add(scenario.CreateTenant(DatabaseProviderType.MySql));
            await catalog.SaveChangesAsync();

            using var scope = scenario.Provider.CreateScope();
            var resolver = scope.ServiceProvider.GetRequiredService<ITenantShardResolver>();
            var shard = await resolver.ResolveByHostAsync(scenario.MySqlHost);

            Assert.NotNull(shard);
            Assert.Equal(scenario.MySqlTenantId, shard!.TenantId);
            Assert.Equal(scenario.MySqlShardKey, shard.ShardKey);
            Assert.Equal(DatabaseProviderType.MySql, shard.DatabaseProvider);
            Assert.Equal(TenantStatus.Active, shard.Status);
        });
    }

    [Fact]
    public async Task MySql_catalog_round_trips_tenant_branding_relationship()
    {
        await RunScenarioAsync(async (scenario, catalog) =>
        {
            catalog.Tenants.Add(scenario.CreateTenant(DatabaseProviderType.MySql));
            catalog.TenantBranding.Add(new TenantBranding
            {
                TenantId = scenario.MySqlTenantId,
                IsPublic = true,
                DisplayName = "MySQL catalog runtime test",
                PrimaryColor = "#123456",
                WelcomeMessage = "Catalog integration test",
                SupportEmail = $"{scenario.MySqlTenantId:N}@mysql-catalog.test",
                SsoEnabled = false
            });

            await catalog.SaveChangesAsync();

            var branding = await catalog.TenantBranding
                .AsNoTracking()
                .Include(value => value.Tenant)
                .SingleAsync(value => value.TenantId == scenario.MySqlTenantId);

            Assert.Equal("MySQL catalog runtime test", branding.DisplayName);
            Assert.True(branding.IsPublic);
            Assert.NotNull(branding.Tenant);
            Assert.Equal(scenario.MySqlTenantId, branding.Tenant.Id);
            Assert.Equal(DatabaseProviderType.MySql, branding.Tenant.DatabaseProvider);
        });
    }

    private static async Task RunScenarioAsync(
        Func<Scenario, HrmsCatalogDbContext, Task> test)
    {
        var connection = Environment.GetEnvironmentVariable("HRMS_MYSQL_CATALOG_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
        {
            throw SkipException.ForSkip(
                "MySQL catalog integration tests not executed: HRMS_MYSQL_CATALOG_TEST_CONNECTION is absent.");
        }

        var scenario = new Scenario(connection);
        try
        {
            await using var scope = scenario.Provider.CreateAsyncScope();
            var catalog = scope.ServiceProvider.GetRequiredService<HrmsCatalogDbContext>();
            await test(scenario, catalog);
        }
        finally
        {
            await scenario.CleanupAsync();
            scenario.Provider.Dispose();
        }
    }

    private sealed class Scenario
    {
        public Scenario(string connection)
        {
            Provider = BuildProvider(connection);
        }

        public ServiceProvider Provider { get; }

        public Guid SqlServerTenantId { get; } = Guid.NewGuid();
        public Guid MySqlTenantId { get; } = Guid.NewGuid();

        public string SqlServerTenantName => $"Catalog MySQL SQL Server {SqlServerTenantId:N}";
        public string MySqlTenantName => $"Catalog MySQL MySQL {MySqlTenantId:N}";

        public string MySqlHost => $"{MySqlTenantId:N}.mysql-catalog.test";
        public string MySqlShardKey => $"mysql-catalog-{MySqlTenantId:N}";

        public IReadOnlySet<Guid> TenantIds =>
            new HashSet<Guid> { SqlServerTenantId, MySqlTenantId };

        public Tenant CreateTenant(DatabaseProviderType provider)
        {
            var tenantId = provider == DatabaseProviderType.MySql
                ? MySqlTenantId
                : SqlServerTenantId;
            var suffix = tenantId.ToString("N");

            return new Tenant
            {
                Id = tenantId,
                TenantCode = $"C{suffix[..19]}",
                Host = $"{suffix}.mysql-catalog.test",
                ShardKey = $"mysql-catalog-{suffix}",
                DatabaseProvider = provider,
                TenantName = provider == DatabaseProviderType.MySql
                    ? MySqlTenantName
                    : SqlServerTenantName,
                Status = TenantStatus.Active
            };
        }

        public async Task CleanupAsync()
        {
            await using var scope = Provider.CreateAsyncScope();
            var catalog = scope.ServiceProvider.GetRequiredService<HrmsCatalogDbContext>();

            // Use tracked deletes here rather than ExecuteDelete with a captured Guid collection. The
            // Oracle provider's bulk-delete parameter binding can throw while materializing that collection;
            // these explicit predicates remain narrow and delete only this scenario's exact rows.
            var branding = await catalog.TenantBranding
                .Where(value =>
                    value.TenantId == SqlServerTenantId ||
                    value.TenantId == MySqlTenantId)
                .ToListAsync();
            catalog.TenantBranding.RemoveRange(branding);

            var tenants = await catalog.Tenants
                .Where(value =>
                    value.Id == SqlServerTenantId ||
                    value.Id == MySqlTenantId)
                .ToListAsync();
            catalog.Tenants.RemoveRange(tenants);

            await catalog.SaveChangesAsync();
        }

        private static ServiceProvider BuildProvider(string connection)
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:CatalogProvider"] = "MySql",
                    ["ConnectionStrings:Catalog"] = connection
                })
                .Build();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddInfrastructure(configuration);
            return services.BuildServiceProvider();
        }
    }
}
