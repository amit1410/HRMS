using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

public class BankConfiguration : IEntityTypeConfiguration<Bank>
{
    public void Configure(EntityTypeBuilder<Bank> builder)
    {
        builder.ToTable("Banks");
        builder.HasKey(b => b.Id);

        builder.Property(b => b.TenantId).IsRequired();
        builder.Property(b => b.Code).IsRequired().HasMaxLength(20);
        builder.Property(b => b.Name).IsRequired().HasMaxLength(100);
        builder.Property(b => b.Description).HasMaxLength(500);
        builder.Property(b => b.ShortName).HasMaxLength(100);
        builder.Property(b => b.IfscPrefix).HasMaxLength(11);
        // Stored as a string so the column stays readable in the database and in exports.
        builder.Property(b => b.BankType).HasConversion<string>().HasMaxLength(30);
        builder.Property(b => b.Country).HasMaxLength(100);
        builder.Property(b => b.Remarks).HasMaxLength(1000);
        builder.Property(b => b.IsActive).IsRequired().HasDefaultValue(true);

        // Code and name are unique per tenant, not globally.
        builder.HasIndex(b => new { b.TenantId, b.Code }).IsUnique();
        builder.HasIndex(b => new { b.TenantId, b.Name }).IsUnique();

        builder.HasAlternateKey(b => new { b.TenantId, b.Id });

        builder.HasOne(b => b.Tenant)
            .WithMany()
            .HasForeignKey(b => b.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
