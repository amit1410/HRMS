using HRMS.Application.Abstractions;
using HRMS.Infrastructure.Persistence;
using HRMS.Infrastructure.Persistence.Catalog;
using HRMS.Infrastructure.Security;
using HRMS.Infrastructure.Sharding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HRMS.Infrastructure;

/// <summary>
/// DI registration for the Infrastructure layer: the two EF Core contexts (provider selected by
/// configuration), host-to-shard resolution, the Application-facing persistence abstractions, the password
/// hasher and the JWT token service.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Bound and validated here rather than through ValidateOnStart, which would pull the hosting
        // abstractions into the persistence layer. Eager is also strictly earlier: a template missing its
        // placeholder fails while services are being registered, before a host exists to serve anything.
        var sharding = configuration.GetSection(ShardingOptions.SectionName).Get<ShardingOptions>()
            ?? new ShardingOptions();

        if (sharding.Validate() is { } problem)
        {
            throw new InvalidOperationException(problem);
        }

        services.AddSingleton(Options.Create(sharding));

        services.AddMemoryCache();
        services.AddScoped<IShardContext, ShardContext>();
        services.AddScoped<ITenantShardResolver, TenantShardResolver>();
        services.AddSingleton<IShardConnectionStringFactory, ShardConnectionStringFactory>();

        // The catalog: one shared database, one connection string, no per-request variation. It is resolved
        // before any tenant is known, so it must never depend on anything request-scoped — which is why it
        // keeps the single-argument overload while the tenant context below does not.
        services.AddDbContext<HrmsCatalogDbContext>(options =>
            UseProvider(options, configuration, CatalogConnectionString(configuration), CatalogHistoryTable));

        // The tenant database, chosen per scope.
        //
        // The two-argument overload is what makes this possible: it hands the options lambda the *scoped*
        // service provider, so the shard selected for this scope can be read while the context's options are
        // being built. The context's own constructor is untouched.
        //
        // Three things silently break it, and none are caught by ValidateOnBuild or ValidateScopes, because
        // the container cannot see inside an options factory. Each fails at runtime with "Cannot resolve
        // scoped service 'IShardContext' from root provider":
        //
        //   * a singleton context lifetime  — AddDbContext(..., ServiceLifetime.Singleton) drags the options
        //     lifetime to singleton with it;
        //   * AddDbContextPool             — passes ServiceLifetime.Singleton unconditionally, so pooling is
        //     simply incompatible with a per-scope connection string;
        //   * AddDbContextFactory          — its lifetime parameter defaults to singleton.
        //
        // So scoped options is load-bearing and those three are forbidden here. ShardConnectionStringTests
        // pins it by asserting two scopes get two different connection strings, because nothing in DI will.
        //
        // The lambda runs once per scope, so it stays cheap and does no I/O: the catalog lookup already
        // happened in middleware, and this only reads its result.
        services.AddDbContext<HrmsDbContext>((serviceProvider, options) =>
        {
            var shard = serviceProvider.GetRequiredService<IShardContext>().Current;
            var connectionString = serviceProvider.GetRequiredService<IShardConnectionStringFactory>().For(shard);

            UseProvider(options, configuration, connectionString);
        });

        // Application services depend on the abstractions; each resolves to the same scoped instance as its
        // context, so a request's changes are tracked and saved together.
        services.AddScoped<IHrmsDbContext>(sp => sp.GetRequiredService<HrmsDbContext>());
        services.AddScoped<IHrmsCatalogDbContext>(sp => sp.GetRequiredService<HrmsCatalogDbContext>());

        services.AddSingleton<IPasswordHasher, IdentityPasswordHasher>();
        services.AddSingleton<IJwtTokenService, JwtTokenService>();

        // Singleton: it creates the scope each organization is provisioned in rather than living in one.
        services.AddSingleton<ITenantProvisioningService, TenantProvisioningService>();

        return services;
    }

    /// <summary>
    /// The catalog's migrations-history table, named rather than left to default.
    /// <para>
    /// Both contexts would otherwise use <c>__EFMigrationsHistory</c>. That is correct while they are in
    /// separate databases, and quietly destructive if anyone ever points them at the same one: two unrelated
    /// migration chains would interleave in one table, and EF would read another chain's applied migrations
    /// as its own — skipping migrations that were never applied, or trying to apply ones that were. Naming
    /// this one makes that configuration merely unusual instead of silently wrong.
    /// </para>
    /// </summary>
    internal const string CatalogHistoryTable = "__EFMigrationsHistoryCatalog";

    /// <summary>
    /// Applies the provider selected by <c>Database:Provider</c> to one connection string. Every context
    /// goes through this, so they can never end up on different providers — a state in which the catalog
    /// would resolve a tenant whose own database could not then be opened.
    /// <para>
    /// The migrations assembly is the same for every context because they all live in this assembly; their
    /// migration <em>chains</em> are still separate, since each context has its own history table.
    /// </para>
    /// </summary>
    private static void UseProvider(
        DbContextOptionsBuilder options,
        IConfiguration configuration,
        string connectionString,
        string? historyTable = null)
    {
        if (ConfiguredProvider.IsSqlite(configuration))
        {
            // No history table to name: the SQLite development path builds the schema from the model with
            // EnsureCreated and never touches migrations.
            options.UseSqlite(connectionString);
            return;
        }

        options.UseSqlServer(connectionString, sql =>
        {
            sql.MigrationsAssembly(typeof(HrmsDbContext).Assembly.FullName);

            if (historyTable is not null)
            {
                sql.MigrationsHistoryTable(historyTable);
            }
        });
    }

    private static string CatalogConnectionString(IConfiguration configuration) =>
        ConfiguredProvider.IsSqlite(configuration)
            ? configuration.GetConnectionString("SqliteCatalog") ?? "Data Source=hrms_catalog_dev.db"
            : configuration.GetConnectionString("Catalog")
                ?? throw new InvalidOperationException("Connection string 'Catalog' is not configured.");
}
