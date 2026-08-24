using HRMS.Application.Abstractions;
using HRMS.Domain.Common;
using HRMS.Domain.Entities;
using HRMS.Infrastructure.Persistence.Conversions;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Infrastructure.Persistence;

/// <summary>
/// EF Core database context for the HRMS. Enforces tenant isolation in two independent ways:
/// (1) global query filters on tenant-scoped entities keyed to the request's <see cref="ITenantContext"/>,
/// and (2) a SaveChanges guard that stamps the server-resolved TenantId onto new rows and treats
/// TenantId as immutable on update, so a client-supplied TenantId can neither place new data in, nor
/// move existing data into, another tenant.
/// </summary>
public class HrmsDbContext : DbContext, IHrmsDbContext
{
    /// <summary>
    /// Configurations under this namespace belong to the catalog database and are excluded from this
    /// model. Both contexts live in one assembly, so the split has to be stated somewhere; stating it
    /// as a namespace means a new catalog configuration is excluded by where it is placed rather than
    /// by someone remembering to add it to a list.
    /// </summary>
    private const string CatalogConfigurationsNamespace = "HRMS.Infrastructure.Persistence.Catalog.Configurations";

    private readonly ITenantContext _tenantContext;

    public HrmsDbContext(DbContextOptions<HrmsDbContext> options, ITenantContext tenantContext)
        : base(options)
    {
        _tenantContext = tenantContext;
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Designation> Designations => Set<Designation>();
    public DbSet<Employee> Employees => Set<Employee>();

    /// <summary>
    /// Applies the UTC treatment to every DateTime property in the model, so a timestamp means the same
    /// thing whether the caller reads it from a freshly saved entity or from a later query.
    /// </summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Every configuration in this assembly except the catalog's. ApplyConfigurationsFromAssembly scans
        // by type, not by namespace, so without the predicate it would find CatalogTenantBrandingConfiguration
        // and map TenantBranding into every shard's model — a table no shard database has. DatabaseInitializer
        // compares the model's tables against the database's and rebuilds on a mismatch, so that would drop
        // and recreate every tenant's database on every startup.
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(HrmsDbContext).Assembly,
            type => type.Namespace?.StartsWith(CatalogConfigurationsNamespace, StringComparison.Ordinal) != true);

        // Belt and braces for the same exclusion, and it has to come *after* the sweep: an explicit
        // configuration applied later overrides an earlier Ignore, silently. With no catalog configuration
        // in the model TenantBranding is only reachable by convention through Tenant.Branding, which
        // TenantConfiguration ignores — but stating the entity-type ignore here means the shard model stays
        // correct even if someone adds a branding navigation somewhere else, instead of quietly regrowing a
        // table that no shard database has.
        modelBuilder.Ignore<TenantBranding>();

        // Tenant global query filters. The predicate references the DbContext instance member, so EF
        // re-evaluates it per query against the current request's tenant. When no tenant is resolved
        // (TenantId == null) the predicate matches no rows: reads must be tenant-scoped, and bootstrap
        // paths (login lookup, seeding) opt out explicitly with IgnoreQueryFilters().
        //
        // These stay in place under database-per-tenant, and they are not redundant there. The shard
        // boundary decides which database a request reaches; these decide which rows within it. Removing
        // them would turn a context with no resolved tenant — startup, seeding, refresh — from "sees
        // nothing" into "sees the entire shard", which is the opposite of the guarantee they exist for.
        //
        // One of the ten DbSets above is absent from this list: Tenant is the root that everything else is
        // scoped *to*, and a shard holds exactly one row of it, so filtering it would only make the tenant
        // invisible to its own login lookup.
        modelBuilder.Entity<User>().HasQueryFilter(e => e.TenantId == _tenantContext.TenantId);
        modelBuilder.Entity<UserRole>().HasQueryFilter(e => e.TenantId == _tenantContext.TenantId);
        modelBuilder.Entity<RefreshToken>().HasQueryFilter(e => e.TenantId == _tenantContext.TenantId);
        modelBuilder.Entity<Department>().HasQueryFilter(e => e.TenantId == _tenantContext.TenantId);
        modelBuilder.Entity<Designation>().HasQueryFilter(e => e.TenantId == _tenantContext.TenantId);
        modelBuilder.Entity<Employee>().HasQueryFilter(e => e.TenantId == _tenantContext.TenantId);
    }

    public override int SaveChanges()
    {
        ApplyAuditAndTenantStamps();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyAuditAndTenantStamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Sets audit timestamps and enforces the tenant guard on tenant-scoped rows — stamping the
    /// resolved tenant on insert and freezing TenantId on update. Called on every save so business/
    /// service code never has to remember to do it.
    /// </summary>
    private void ApplyAuditAndTenantStamps()
    {
        var utcNow = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries())
        {
            // Defense in depth for tenant-scoped rows (spec: isolate on create AND update).
            if (entry.Entity is ITenantEntity tenantEntity)
            {
                if (entry.State == EntityState.Added && _tenantContext.TenantId is Guid tenantId)
                {
                    // Force new rows onto the server-resolved tenant, ignoring any client-supplied TenantId.
                    tenantEntity.TenantId = tenantId;
                }
                else if (entry.State == EntityState.Modified)
                {
                    // TenantId is immutable after insert: an update can never relocate a row to another tenant.
                    entry.Property(nameof(ITenantEntity.TenantId)).IsModified = false;
                }
            }

            if (entry.Entity is BaseEntity auditable)
            {
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
}
