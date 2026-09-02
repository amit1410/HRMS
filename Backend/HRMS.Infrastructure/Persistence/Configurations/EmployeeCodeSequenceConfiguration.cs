using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

public sealed class EmployeeCodeSequenceConfiguration : IEntityTypeConfiguration<EmployeeCodeSequence>
{
    public void Configure(EntityTypeBuilder<EmployeeCodeSequence> builder)
    {
        builder.ToTable("EmployeeCodeSequences");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.TenantId).IsRequired();
        builder.Property(e => e.Scope).HasConversion<int>().IsRequired();
        builder.Property(e => e.ScopeKey).IsRequired().HasMaxLength(200);
        builder.Property(e => e.NextNumber).IsRequired();
        builder.Property(e => e.IncrementBy).IsRequired();
        builder.Property(e => e.ResetPeriod).HasConversion<int>().IsRequired();
        builder.Property(e => e.PeriodKey).IsRequired().HasMaxLength(32);
        builder.Property(e => e.RowVersion).IsRowVersion();
        builder.HasIndex(e => new { e.TenantId, e.EmployeeCodeRuleId, e.Scope, e.ScopeKey, e.PeriodKey }).IsUnique();
        builder.HasOne(e => e.Tenant).WithMany().HasForeignKey(e => e.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.Rule).WithMany()
            .HasForeignKey(e => new { e.TenantId, e.EmployeeCodeRuleId })
            .HasPrincipalKey(e => new { e.TenantId, e.Id })
            .OnDelete(DeleteBehavior.Cascade);
    }
}
