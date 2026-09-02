using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

/// <summary>
/// Employee code generation configuration. One row per tenant; the <c>(TenantId, Id)</c> alternate key and
/// the composite tenant foreign key keep the row isolated exactly like every other tenant-scoped entity.
/// </summary>
public class EmployeeCodeConfigConfiguration : IEntityTypeConfiguration<EmployeeCodeConfig>
{
    public void Configure(EntityTypeBuilder<EmployeeCodeConfig> builder)
    {
        builder.ToTable("EmployeeCodeConfigs");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.TenantId).IsRequired();
        builder.Property(e => e.Prefix).IsRequired().HasMaxLength(10);
        builder.Property(e => e.AutoGenerate).IsRequired();
        builder.Property(e => e.AssignmentMode).IsRequired();
        builder.Property(e => e.GenerationMethod).IsRequired(false);
        builder.Property(e => e.NextNumber).IsRequired();
        builder.Property(e => e.Padding).IsRequired();
        builder.Property(e => e.Separator).IsRequired().HasMaxLength(1);
        builder.Property(e => e.EffectiveFrom).IsRequired();

        builder.HasAlternateKey(e => new { e.TenantId, e.Id });

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
