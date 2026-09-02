using HRMS.Application.Abstractions;
using HRMS.Infrastructure.Persistence.Catalog;
using HRMS.Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HRMS.Infrastructure.Persistence;

/// <summary>
/// Brings the databases up to date and seeds them at application startup.
/// <para>
/// The order is forced by the architecture. The catalog is what maps a host to an organization and names the
/// database that organization's data lives in, so it is prepared and seeded first, and then <em>enumerated</em>
/// — there is no other way to know which tenant databases exist. Each organization is then provisioned in
/// its own scope, because the tenant context's connection is chosen per scope.
/// </para>
/// <para>
/// The two failure modes are deliberately different. An unreachable <b>catalog</b> is fatal: nothing can be
/// routed, so no one can sign in anywhere, and starting up would only produce a server that fails every
/// request. An unreachable <b>single tenant database</b> is logged and skipped: every other organization is
/// still served normally, and the affected one is refused at its own connection rather than shown someone
/// else's data. Treating one customer's broken database as fatal would take every customer offline with it.
/// </para>
/// </summary>
public static class DatabaseInitializer
{
    /// <summary>
    /// Runs startup database preparation unless the explicit development-only skip switch is enabled.
    /// The switch is intentionally passed in by the host so production can never skip initialization by
    /// accident, and the default remains the existing initialization path.
    /// </summary>
    public static async Task InitializeIfEnabledAsync(
        IServiceProvider services,
        bool isDevelopment,
        bool skipInitialization,
        CancellationToken cancellationToken = default)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("HRMS.DatabaseInitializer");

        if (isDevelopment && skipInitialization)
        {
            logger.LogWarning(
                "Database initialization was skipped because Database:SkipInitialization is enabled in Development. "
                + "Schema preparation and seeding were not performed.");
            return;
        }

        await InitializeAsync(services, cancellationToken);
    }

    public static async Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("HRMS.DatabaseInitializer");

        var organizations = await InitializeCatalogAsync(services, logger, cancellationToken);

        var provisioning = services.GetRequiredService<ITenantProvisioningService>();
        var provisioned = 0;

        foreach (var organization in organizations)
        {
            try
            {
                await provisioning.ProvisionAsync(organization, cancellationToken);
                provisioned++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(
                    ex,
                    "Could not provision organization {TenantCode} on shard {ShardKey}. Every other "
                    + "organization is unaffected; this one cannot serve requests until the failure is fixed "
                    + "and the application is restarted.",
                    organization.TenantCode,
                    organization.ShardKey);
            }
        }

        logger.LogInformation(
            "Database initialization complete: {Provisioned} of {Total} organization(s) provisioned.",
            provisioned,
            organizations.Count);
    }

    /// <summary>
    /// Prepares and seeds the catalog, and returns the organizations it holds. Failures propagate — see the
    /// class remarks for why this one is fatal.
    /// </summary>
    private static async Task<List<ShardDescriptor>> InitializeCatalogAsync(
        IServiceProvider services,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<HrmsCatalogDbContext>();

        await SchemaPreparer.PrepareAsync(catalog, "catalog", logger, cancellationToken);

        logger.LogInformation("Seeding catalog organizations and branding.");
        await DatabaseSeeder.SeedCatalogAsync(catalog, cancellationToken);

        // Inactive organizations are provisioned too. Their databases have to be current for a suspension to
        // be reversible by flipping a status back — and a schema left behind by an older model would
        // otherwise turn "reactivate this customer" into a migration exercise.
        var organizations = await catalog.Tenants
            .AsNoTracking()
            .OrderBy(tenant => tenant.TenantCode)
            .Select(tenant => new ShardDescriptor(
                tenant.Id, tenant.TenantCode, tenant.Host, tenant.ShardKey, tenant.Status))
            .ToListAsync(cancellationToken);

        logger.LogInformation("The catalog holds {Count} organization(s).", organizations.Count);
        return organizations;
    }
}
