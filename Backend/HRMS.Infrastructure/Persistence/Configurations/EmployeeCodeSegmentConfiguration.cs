using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

public sealed class EmployeeCodeSegmentConfiguration : IEntityTypeConfiguration<EmployeeCodeSegment>
{
    public void Configure(EntityTypeBuilder<EmployeeCodeSegment> builder)
    {
        builder.ToTable("EmployeeCodeSegments");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.TenantId).IsRequired();
        builder.Property(e => e.SegmentType).HasConversion<int>().IsRequired();
        builder.Property(e => e.FixedValue).HasMaxLength(100);
        builder.HasIndex(e => new { e.TenantId, e.EmployeeCodeRuleId, e.SequenceOrder }).IsUnique();
        builder.HasOne(e => e.Tenant).WithMany().HasForeignKey(e => e.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.Rule).WithMany(e => e.Segments)
            .HasForeignKey(e => new { e.TenantId, e.EmployeeCodeRuleId })
            .HasPrincipalKey(e => new { e.TenantId, e.Id })
            .OnDelete(DeleteBehavior.Cascade);
    }
}
