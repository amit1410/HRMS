using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

public sealed class EmployeeCodeRuleConditionConfiguration : IEntityTypeConfiguration<EmployeeCodeRuleCondition>
{
    public void Configure(EntityTypeBuilder<EmployeeCodeRuleCondition> builder)
    {
        builder.ToTable("EmployeeCodeRuleConditions");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.TenantId).IsRequired();
        builder.Property(e => e.Field).HasConversion<int>().IsRequired();
        builder.Property(e => e.Operator).HasConversion<int>().IsRequired();
        builder.Property(e => e.Value).HasMaxLength(200);
        builder.HasIndex(e => new { e.TenantId, e.EmployeeCodeRuleId });
        builder.HasOne(e => e.Tenant).WithMany().HasForeignKey(e => e.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.Rule).WithMany(e => e.Conditions)
            .HasForeignKey(e => new { e.TenantId, e.EmployeeCodeRuleId })
            .HasPrincipalKey(e => new { e.TenantId, e.Id })
            .OnDelete(DeleteBehavior.Cascade);
    }
}
