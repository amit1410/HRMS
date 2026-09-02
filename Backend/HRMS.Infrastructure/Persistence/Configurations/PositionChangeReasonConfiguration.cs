using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

public class PositionChangeReasonConfiguration : IEntityTypeConfiguration<PositionChangeReason>
{
    public void Configure(EntityTypeBuilder<PositionChangeReason> builder)
    {
        builder.ToTable("PositionChangeReasons");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.TenantId).IsRequired();
        builder.Property(r => r.Code).IsRequired().HasMaxLength(20);
        builder.Property(r => r.Name).IsRequired().HasMaxLength(100);
        builder.Property(r => r.Description).HasMaxLength(500);
        builder.Property(r => r.IsActive).IsRequired().HasDefaultValue(true);
        builder.Property(r => r.SortOrder).IsRequired().HasDefaultValue(0);

        builder.HasIndex(r => new { r.TenantId, r.Code }).IsUnique();
        builder.HasIndex(r => new { r.TenantId, r.Name }).IsUnique();

        builder.HasAlternateKey(r => new { r.TenantId, r.Id });

        builder.HasOne(r => r.Tenant)
            .WithMany()
            .HasForeignKey(r => r.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
