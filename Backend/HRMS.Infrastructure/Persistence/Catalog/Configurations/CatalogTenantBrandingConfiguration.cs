using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Catalog.Configurations;

/// <summary>
/// Maps <see cref="TenantBranding"/> as a 1:1 extension of the catalog's <c>Tenants</c>: the tenant's own
/// id is the primary key, so the relationship cannot be duplicated or orphaned by construction.
/// <para>
/// Catalog-only. Branding is read to decide what the sign-in screen looks like, which happens before
/// anyone is authenticated and therefore before a shard has been chosen — so it has to sit beside the
/// routing table rather than inside the data it precedes.
/// </para>
/// </summary>
public class CatalogTenantBrandingConfiguration : IEntityTypeConfiguration<TenantBranding>
{
    public void Configure(EntityTypeBuilder<TenantBranding> builder)
    {
        builder.ToTable("TenantBranding");

        // The FK is the PK. A tenant has one branding row or none, and there is no surrogate id to keep
        // in step with the tenant it belongs to. The relationship itself is declared from the Tenant side
        // in CatalogTenantConfiguration, so it is stated exactly once.
        builder.HasKey(b => b.TenantId);

        builder.Property(b => b.IsPublic).IsRequired();
        builder.Property(b => b.DisplayName).HasMaxLength(100);
        builder.Property(b => b.LogoUrl).HasMaxLength(512);

        // "#RRGGBB" — seven characters, and the read path rejects anything that is not exactly that shape.
        builder.Property(b => b.PrimaryColor).HasMaxLength(7);

        builder.Property(b => b.WelcomeMessage).HasMaxLength(160);
        builder.Property(b => b.SupportEmail).HasMaxLength(256);
        builder.Property(b => b.SsoEnabled).IsRequired();
        builder.Property(b => b.SsoProviderName).HasMaxLength(50);
    }
}
