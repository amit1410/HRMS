using HRMS.Domain.Entities;
using HRMS.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Catalog.Configurations;

/// <summary>
/// Maps <c>Tenants</c> for the <em>catalog</em> database, where it holds every organization and acts as
/// the routing table: a request's host is matched against <see cref="Tenant.Host"/> to discover which
/// database to open.
/// <para>
/// The catalog cannot live inside a tenant's own database, because you would have to connect before
/// knowing where to connect.
/// </para>
/// </summary>
public class CatalogTenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        TenantMapping.ApplyColumns(builder);

        // Users live in the shard, not here. Ignoring the navigation is required rather than cosmetic: EF
        // discovers entity types through navigations, so leaving it would pull User — and through User,
        // UserRole, RefreshToken and the whole employee graph — into the routing database's model.
        builder.Ignore(t => t.Users);

        builder.HasOne(t => t.Branding)
            .WithOne(b => b.Tenant)
            .HasForeignKey<TenantBranding>(b => b.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
