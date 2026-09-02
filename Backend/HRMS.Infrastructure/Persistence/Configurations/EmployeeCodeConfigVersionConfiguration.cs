using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

public sealed class EmployeeCodeConfigVersionConfiguration : IEntityTypeConfiguration<EmployeeCodeConfigVersion>
{
    public void Configure(EntityTypeBuilder<EmployeeCodeConfigVersion> builder)
    {
        builder.ToTable("EmployeeCodeConfigVersions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Prefix).HasMaxLength(10).IsRequired();
        builder.Property(x => x.AssignmentMode).IsRequired();
        builder.Property(x => x.GenerationMethod).IsRequired(false);
        builder.Property(x => x.Separator).HasMaxLength(1).IsRequired();
        builder.Property(x => x.EffectiveFrom).IsRequired();
        builder.HasAlternateKey(x => new { x.TenantId, x.Id });
        builder.HasIndex(x => new { x.TenantId, x.EmployeeCodeConfigId, x.EffectiveFrom });
        builder.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Configuration).WithMany().HasForeignKey(x => new { x.TenantId, x.EmployeeCodeConfigId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Rules).WithOne(x => x.ConfigurationVersion).HasForeignKey(x => new { x.TenantId, x.EmployeeCodeConfigVersionId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
