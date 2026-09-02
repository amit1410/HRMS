using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

public class HoldingCompanyConfiguration : IEntityTypeConfiguration<HoldingCompany>
{
    public void Configure(EntityTypeBuilder<HoldingCompany> builder)
    {
        builder.ToTable("HoldingCompanies");
        builder.HasKey(h => h.Id);

        builder.Property(h => h.TenantId).IsRequired();
        builder.Property(h => h.Code).IsRequired().HasMaxLength(20);
        builder.Property(h => h.Name).IsRequired().HasMaxLength(100);
        builder.Property(h => h.Description).HasMaxLength(500);
        builder.Property(h => h.IsActive).IsRequired().HasDefaultValue(true);

        builder.HasIndex(h => new { h.TenantId, h.Code }).IsUnique();
        builder.HasIndex(h => new { h.TenantId, h.Name }).IsUnique();

        builder.HasAlternateKey(h => new { h.TenantId, h.Id });

        builder.HasOne(h => h.Tenant)
            .WithMany()
            .HasForeignKey(h => h.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
