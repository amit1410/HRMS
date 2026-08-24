using HRMS.Application.Abstractions;
using HRMS.Domain.Entities;
using HRMS.Infrastructure.Persistence.Catalog;
using HRMS.Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HRMS.Infrastructure.Persistence;

/// <inheritdoc cref="ITenantProvisioningService"/>
public sealed class TenantProvisioningService : ITenantProvisioningService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TenantProvisioningService> _logger;

    public TenantProvisioningService(
        IServiceScopeFactory scopeFactory,
        ILogger<TenantProvisioningService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task ProvisionAsync(ShardDescriptor shard, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shard);

        // Its own scope, and that is the whole mechanism rather than tidiness. HrmsDbContext's options are
        // built once per scope from whatever IShardContext holds at that moment, so the shard has to be
        // selected before the context is first resolved — and a scope is write-once, so one scope can only
        // ever provision one organization. Calling this in a loop therefore cannot leak the previous
        // organization's connection into the next one's seeding.
        using var scope = _scopeFactory.CreateScope();
        var provider = scope.ServiceProvider;

        provider.GetRequiredService<IShardContext>().Use(shard);

        var tenant = await LoadCatalogRowAsync(provider, shard, cancellationToken);

        var db = provider.GetRequiredService<HrmsDbContext>();
        await SchemaPreparer.PrepareAsync(db, $"'{shard.TenantCode}' tenant", _logger, cancellationToken);

        _logger.LogInformation(
            "Seeding organization {TenantCode} on shard {ShardKey}.", shard.TenantCode, shard.ShardKey);

        await DatabaseSeeder.SeedShardAsync(
            db,
            provider.GetRequiredService<IPasswordHasher>(),
            tenant,
            cancellationToken);
    }

    /// <summary>
    /// The organization's row as the catalog holds it, detached.
    /// <para>
    /// Read from the catalog rather than rebuilt from the descriptor or from <see cref="SeedData"/>: the
    /// tenant row a shard carries exists to satisfy its own foreign keys and must say the same thing the
    /// catalog says, and the catalog is the authority. Reading it here is also what lets this provision an
    /// organization created through onboarding, which has no seed data to be rebuilt from at all.
    /// </para>
    /// <para>
    /// <c>AsNoTracking</c> matters beyond performance: the instance is handed to a <em>different</em>
    /// context to insert, and an entity still tracked by the catalog cannot be.
    /// </para>
    /// </summary>
    private static async Task<Tenant> LoadCatalogRowAsync(
        IServiceProvider provider,
        ShardDescriptor shard,
        CancellationToken cancellationToken)
    {
        var catalog = provider.GetRequiredService<HrmsCatalogDbContext>();

        return await catalog.Tenants
                   .AsNoTracking()
                   .SingleOrDefaultAsync(tenant => tenant.Id == shard.TenantId, cancellationToken)
               ?? throw new InvalidOperationException(
                   $"Organization '{shard.TenantCode}' ({shard.TenantId}) has no row in the catalog, so its "
                   + "database cannot be provisioned. The catalog row has to be written first: it is what "
                   + "decides which database the organization's data belongs in.");
    }
}
