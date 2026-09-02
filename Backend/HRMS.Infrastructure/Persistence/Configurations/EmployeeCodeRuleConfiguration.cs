using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

public sealed class EmployeeCodeRuleConfiguration : IEntityTypeConfiguration<EmployeeCodeRule>
{
    public void Configure(EntityTypeBuilder<EmployeeCodeRule> builder)
    {
        builder.ToTable("EmployeeCodeRules");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.TenantId).IsRequired();
        builder.Property(e => e.Name).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Status).HasConversion<int>().IsRequired();
        builder.Property(e => e.IsDeleted).IsRequired();
        builder.Property(e => e.DeletedAt);
        builder.HasIndex(e => new { e.TenantId, e.EmployeeCodeConfigId, e.Priority });
        builder.HasAlternateKey(e => new { e.TenantId, e.Id });
        builder.HasOne(e => e.Tenant).WithMany().HasForeignKey(e => e.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.Configuration).WithMany(e => e.Rules)
            .HasForeignKey(e => new { e.TenantId, e.EmployeeCodeConfigId })
            .HasPrincipalKey(e => new { e.TenantId, e.Id })
            .OnDelete(DeleteBehavior.Cascade);
    }
}
