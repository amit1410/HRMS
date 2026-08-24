using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <c>Tenants</c> for a <em>shard</em> database, where it holds exactly one row: the organization
/// that owns the database.
/// <para>
/// That single row is not redundant. Four foreign keys (<c>Users</c>, <c>Departments</c>,
/// <c>Designations</c>, <c>Employees</c>) name it as their principal, and SQL Server has no cross-database
/// foreign keys — so without it the shard loses referential integrity to its own tenant, and every query
/// that reads <c>Tenants</c> would need rewriting to reach the catalog instead.
/// </para>
/// </summary>
public class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        TenantMapping.ApplyColumns(builder);

        // Branding is catalog-only: it is read to draw the sign-in screen, before there is a token and
        // therefore before there is a shard to read it from. Ignoring the navigation is required, not
        // tidiness — EF discovers entity types through navigations, so leaving it would map TenantBranding
        // into every shard's model, and DatabaseInitializer would then see a table the shard database does
        // not have and drop the whole database on every startup.
        builder.Ignore(t => t.Branding);

        builder.HasMany(t => t.Users)
            .WithOne(u => u.Tenant)
            .HasForeignKey(u => u.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
