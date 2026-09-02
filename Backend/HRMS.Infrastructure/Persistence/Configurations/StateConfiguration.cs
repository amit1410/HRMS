using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

public class StateConfiguration : IEntityTypeConfiguration<State>
{
    public void Configure(EntityTypeBuilder<State> builder)
    {
        builder.ToTable("States");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.CountryId).IsRequired();
        builder.Property(s => s.Code).IsRequired().HasMaxLength(10);
        builder.Property(s => s.Name).IsRequired().HasMaxLength(100);
        builder.Property(s => s.IsActive).IsRequired().HasDefaultValue(true);

        // Code is unique within a country.
        builder.HasIndex(s => new { s.CountryId, s.Code }).IsUnique();
        builder.HasIndex(s => new { s.CountryId, s.Name }).IsUnique();

        builder.HasOne(s => s.Country)
            .WithMany(c => c.States)
            .HasForeignKey(s => s.CountryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
