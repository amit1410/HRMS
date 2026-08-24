using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

/// <summary>
/// The column shape of <c>Tenants</c>, shared by the two contexts that map it.
/// <para>
/// <c>Tenants</c> exists in the catalog database (every organization) and in each shard database (that
/// organization's own row). The two must agree on every column: a row is written by provisioning against
/// the catalog and replicated into the shard, so a max length that differs between them is a truncation
/// or a constraint violation waiting for the first long name. Keeping the columns here means there is one
/// place to change, and the navigations — which are genuinely different per context — stay with the
/// configuration that owns them.
/// </para>
/// </summary>
internal static class TenantMapping
{
    /// <summary>A hostname's maximum length. <c>Host</c> holds a whole host, not one label.</summary>
    internal const int HostMaxLength = 253;

    internal const int TenantCodeMaxLength = 20;
    internal const int ShardKeyMaxLength = 64;

    /// <summary>
    /// Applies the table, key, columns and unique indexes. Does <em>not</em> touch navigations: the shard
    /// maps <c>Users</c> and ignores <c>Branding</c>, and the catalog does the opposite.
    /// </summary>
    internal static void ApplyColumns(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("Tenants");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.TenantCode).IsRequired().HasMaxLength(TenantCodeMaxLength);
        builder.Property(t => t.Host).IsRequired().HasMaxLength(HostMaxLength);
        builder.Property(t => t.ShardKey).IsRequired().HasMaxLength(ShardKeyMaxLength);
        builder.Property(t => t.TenantName).IsRequired().HasMaxLength(200);
        builder.Property(t => t.Email).HasMaxLength(256);
        builder.Property(t => t.Phone).HasMaxLength(30);
        builder.Property(t => t.Address).HasMaxLength(500);
        builder.Property(t => t.Status).HasConversion<int>();

        // The operator-facing label: still globally unique, still what logs and support tickets say.
        builder.HasIndex(t => t.TenantCode).IsUnique();

        // The routing key. Unique because a host that resolved to two organizations would be an ambiguity
        // no request could settle — and because in the catalog this index *is* the tenant lookup.
        builder.HasIndex(t => t.Host).IsUnique();

        // One database per organization, so no two organizations may name the same one.
        builder.HasIndex(t => t.ShardKey).IsUnique();
    }
}
