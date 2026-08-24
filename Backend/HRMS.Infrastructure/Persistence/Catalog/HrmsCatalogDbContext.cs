using HRMS.Application.Abstractions;
using HRMS.Domain.Common;
using HRMS.Domain.Entities;
using HRMS.Infrastructure.Persistence.Catalog.Configurations;
using HRMS.Infrastructure.Persistence.Conversions;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Infrastructure.Persistence.Catalog;

/// <summary>
/// The tenant catalog: the one database shared by every organization, holding only what has to be
/// readable <em>before</em> the system knows which tenant database to open.
/// <para>
/// That is two tables. <c>Tenants</c> maps a request's host to a shard key, so it is the routing table and
/// necessarily cannot live inside the databases it routes to. <c>TenantBranding</c> decides what the
/// sign-in screen looks like, which is read while the caller is still anonymous.
/// </para>
/// <para>
/// There are no query filters here and there is no tenant stamp, because there is no tenant to scope to:
/// this context is by definition cross-tenant. That makes it the one place in the persistence layer where
/// isolation is not enforced by the context, so <b>every</b> read through it should be a lookup of a
/// single known tenant — never a listing handed to a caller. Anything that needs a tenant's own data
/// belongs on <see cref="HrmsDbContext"/>, where the shard boundary and the query filters both apply.
/// </para>
/// </summary>
public class HrmsCatalogDbContext : DbContext, IHrmsCatalogDbContext
{
    public HrmsCatalogDbContext(DbContextOptions<HrmsCatalogDbContext> options)
        : base(options)
    {
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantBranding> TenantBranding => Set<TenantBranding>();

    /// <summary>
    /// Matches <see cref="HrmsDbContext.ConfigureConventions"/>: a timestamp means the same thing in both
    /// databases, so a tenant row replicated from here into a shard does not change meaning on the way.
    /// </summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Applied one by one, deliberately. ApplyConfigurationsFromAssembly — plain or with a predicate —
        // would sweep up the shard configurations that live in this assembly and map the whole employee
        // graph into the routing database.
        modelBuilder.ApplyConfiguration(new CatalogTenantConfiguration());
        modelBuilder.ApplyConfiguration(new CatalogTenantBrandingConfiguration());

        // Ordering is load-bearing: an Ignore placed *before* a configuration is silently undone by it, so
        // this has to sit after the two calls above. Tenant.Users is already ignored inside
        // CatalogTenantConfiguration, which keeps User out by convention; this states it as an entity-type
        // ignore as well, because getting it wrong fails open — the catalog would simply grow the ten shard
        // tables via navigation discovery rather than throwing. CatalogModelTests pins the result.
        modelBuilder.Ignore<User>();

    }

    public override int SaveChanges()
    {
        ApplyAuditTimestamps();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyAuditTimestamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// The audit half of <c>HrmsDbContext.ApplyAuditAndTenantStamps</c>, and only that half. Nothing here
    /// implements <c>ITenantEntity</c>, so there is no tenant to stamp — but <see cref="Tenant"/> is a
    /// <see cref="BaseEntity"/>, and a tenant created through the catalog should carry the same
    /// timestamps as one created anywhere else.
    /// </summary>
    private void ApplyAuditTimestamps()
    {
        var utcNow = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is not BaseEntity auditable)
            {
                continue;
            }

            switch (entry.State)
            {
                case EntityState.Added:
                    if (auditable.CreatedDate == default)
                    {
                        auditable.CreatedDate = utcNow;
                    }
                    break;
                case EntityState.Modified:
                    auditable.ModifiedDate = utcNow;
                    entry.Property(nameof(BaseEntity.CreatedDate)).IsModified = false;
                    break;
            }
        }
    }
}
