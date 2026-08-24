using HRMS.Application.Abstractions;
using HRMS.Domain.Enums;
using HRMS.Infrastructure;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRMS.Tests;

/// <summary>
/// Which database a tenant <c>DbContext</c> opens, decided per scope.
/// <para>
/// This is the one seam in the system that dependency injection cannot check for us. The connection string
/// is chosen inside an options factory, which is an opaque delegate as far as the container is concerned —
/// so <c>ValidateOnBuild</c> and <c>ValidateScopes</c> both pass whether the registration reads a per-scope
/// shard or quietly ignores it. Switching to <c>AddDbContextPool</c>, to <c>AddDbContextFactory</c>, or to a
/// singleton context lifetime would each break it at runtime with nothing failing at startup. These tests
/// are the check that DI cannot perform.
/// </para>
/// </summary>
public class ShardConnectionStringTests
{
    private const string SharedConnectionString = "Data Source=hrms-shared-not-opened.db";
    private const string Template = "Data Source=hrms-{shardKey}-not-opened.db";

    /// <summary>
    /// The point of the whole change: two organizations, two scopes, two databases, one code path. Nothing
    /// here connects — <c>GetConnectionString</c> reads the configured value, so no files are created.
    /// </summary>
    [Fact]
    public void Two_organizations_in_two_scopes_open_two_databases()
    {
        using var provider = BuildProvider(Template);

        var first = ConnectionStringFor(provider, Shard("demo01"));
        var second = ConnectionStringFor(provider, Shard("demo02"));

        Assert.NotEqual(first, second);
        Assert.Contains("demo01", first);
        Assert.Contains("demo02", second);
    }

    /// <summary>
    /// Fails closed. There is no default tenant database to fall back to, because falling back is the failure
    /// where one organization's rows are written into another organization's database.
    /// </summary>
    [Fact]
    public void A_scope_with_no_organization_has_no_tenant_database_to_open()
    {
        using var provider = BuildProvider(Template);

        var exception = Assert.Throws<InvalidOperationException>(() => ConnectionStringFor(provider, shard: null));

        // The message has to name the way out, because the callers that hit it are startup code paths —
        // provisioning, seeding, design-time tooling — not requests.
        Assert.Contains(nameof(IShardContext.Use), exception.Message);
    }

    /// <summary>
    /// The other supported mode: no template, so every organization shares the configured database and the
    /// global query filters are what keep them apart. This is what this system did before sharding existed,
    /// which is why an upgrade does not force a data split on day one.
    /// </summary>
    [Fact]
    public void Without_a_template_every_organization_shares_the_configured_database()
    {
        using var provider = BuildProvider(template: null);

        Assert.Equal(SharedConnectionString, ConnectionStringFor(provider, Shard("demo01")));
        Assert.Equal(SharedConnectionString, ConnectionStringFor(provider, Shard("demo02")));

        // And a scope with no organization still works, which is what keeps startup and seeding running.
        Assert.Equal(SharedConnectionString, ConnectionStringFor(provider, shard: null));
    }

    /// <summary>
    /// A shard key is substituted into a connection string, so it is a template-injection sink. The value
    /// comes from the catalog database, and a connection string assembled from database content must not
    /// trust the content: a ';' would let one admin-entered field append credentials or repoint the data
    /// source entirely.
    /// </summary>
    [Theory]
    [InlineData("demo01;Password=hunter2")]
    [InlineData("demo01.db;Mode=ReadWrite")]
    [InlineData("../../etc/passwd")]
    [InlineData("DEMO01")]
    [InlineData("-demo01")]
    [InlineData("")]
    public void A_shard_key_that_is_not_a_safe_database_name_is_refused(string shardKey)
    {
        using var provider = BuildProvider(Template);

        Assert.Throws<InvalidOperationException>(() => ConnectionStringFor(provider, Shard(shardKey)));
    }

    [Theory]
    [InlineData("demo01")]
    [InlineData("acme-hr")]
    [InlineData("tenant_42")]
    [InlineData("9lives")]
    public void An_ordinary_shard_key_is_accepted(string shardKey)
    {
        using var provider = BuildProvider(Template);

        Assert.Contains(shardKey, ConnectionStringFor(provider, Shard(shardKey)));
    }

    /// <summary>
    /// The worst possible typo, refused where it is still obvious. A template missing its placeholder
    /// resolves for every organization, connects for every organization, and hands every organization the
    /// same database — and nothing downstream can tell, because the query filters would keep the rows apart
    /// and it would look like it worked.
    /// </summary>
    [Fact]
    public void A_template_that_forgot_the_placeholder_is_refused_before_the_host_is_built()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => BuildProvider("Data Source=hrms.db"));

        Assert.Contains("{shardKey}", exception.Message);
    }

    private static ShardDescriptor Shard(string shardKey) =>
        new(Guid.NewGuid(), shardKey.ToUpperInvariant(), $"{shardKey}.localhost", shardKey, TenantStatus.Active);

    private static string? ConnectionStringFor(IServiceProvider provider, ShardDescriptor? shard)
    {
        using var scope = provider.CreateScope();

        if (shard is not null)
        {
            scope.ServiceProvider.GetRequiredService<IShardContext>().Use(shard);
        }

        return scope.ServiceProvider.GetRequiredService<HrmsDbContext>().Database.GetConnectionString();
    }

    private static ServiceProvider BuildProvider(string? template)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Database:Provider"] = "Sqlite",
            ["ConnectionStrings:Sqlite"] = SharedConnectionString,
            ["ConnectionStrings:SqliteCatalog"] = "Data Source=hrms-catalog-not-opened.db"
        };

        if (template is not null)
        {
            settings["Sharding:SqliteConnectionStringTemplate"] = template;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddLogging();

        // Stand-ins for what the composition root normally supplies, needed because ValidateOnBuild below
        // checks every descriptor rather than only the ones under test: IConfiguration comes from the generic
        // host, and TimeProvider from AddApplication().
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(TimeProvider.System);

        services.AddScoped<ITenantContext>(_ => new TestTenantContext());
        services.AddInfrastructure(configuration);

        // Validation is on so that a genuine captive-dependency mistake in the registration — a singleton
        // reaching for the scoped shard — fails here rather than in production. It cannot see into the
        // options factory, which is exactly why the tests above exist.
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
    }
}
