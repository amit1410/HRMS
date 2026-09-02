using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

public class LobConfiguration : IEntityTypeConfiguration<Lob>
{
    public void Configure(EntityTypeBuilder<Lob> builder)
    {
        builder.ToTable("LinesOfBusiness");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.TenantId).IsRequired();
        builder.Property(l => l.Code).IsRequired().HasMaxLength(20);
        builder.Property(l => l.Name).IsRequired().HasMaxLength(100);
        builder.Property(l => l.Description).HasMaxLength(500);
        builder.Property(l => l.IsActive).IsRequired().HasDefaultValue(true);

        builder.HasIndex(l => new { l.TenantId, l.Code }).IsUnique();
        builder.HasIndex(l => new { l.TenantId, l.Name }).IsUnique();

        builder.HasAlternateKey(l => new { l.TenantId, l.Id });

        builder.HasOne(l => l.Tenant)
            .WithMany()
            .HasForeignKey(l => l.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(l => l.HoldingCompany)
            .WithMany()
            .HasForeignKey(l => l.HoldingCompanyId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
