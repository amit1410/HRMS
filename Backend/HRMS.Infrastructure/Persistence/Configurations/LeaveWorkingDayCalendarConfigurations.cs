using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

public sealed class HolidayConfiguration : IEntityTypeConfiguration<Holiday>
{
    public void Configure(EntityTypeBuilder<Holiday> builder)
    {
        builder.ToTable("Holidays");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.Name).IsRequired().HasMaxLength(200);
        builder.Property(x => x.Date).HasColumnType("date").IsRequired();
        builder.Property(x => x.IsActive).IsRequired().HasDefaultValue(true);
        builder.Property(x => x.CreatedBy).HasMaxLength(200);
        builder.Property(x => x.ModifiedBy).HasMaxLength(200);
        builder.HasAlternateKey(x => new { x.TenantId, x.Id });
        builder.HasIndex(x => new { x.TenantId, x.Date, x.WorkLocationId, x.CountryLocationId });
        builder.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.CountryLocation).WithMany().HasForeignKey(x => x.CountryLocationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.WorkLocation).WithMany().HasForeignKey(x => new { x.TenantId, x.WorkLocationId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class WeeklyOffConfigurationConfiguration : IEntityTypeConfiguration<WeeklyOffConfiguration>
{
    public void Configure(EntityTypeBuilder<WeeklyOffConfiguration> builder)
    {
        builder.ToTable("WeeklyOffConfigurations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.EffectiveFrom).HasColumnType("date").IsRequired();
        builder.Property(x => x.EffectiveTo).HasColumnType("date");
        builder.Property(x => x.IsActive).IsRequired().HasDefaultValue(true);
        builder.Property(x => x.CreatedBy).HasMaxLength(200);
        builder.Property(x => x.ModifiedBy).HasMaxLength(200);
        builder.HasAlternateKey(x => new { x.TenantId, x.Id });
        builder.HasIndex(x => new { x.TenantId, x.EffectiveFrom, x.EffectiveTo, x.WorkLocationId, x.CountryLocationId });
        builder.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.CountryLocation).WithMany().HasForeignKey(x => x.CountryLocationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.WorkLocation).WithMany().HasForeignKey(x => new { x.TenantId, x.WorkLocationId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Days).WithOne(x => x.WeeklyOffConfiguration).HasForeignKey(x => new { x.TenantId, x.WeeklyOffConfigurationId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class WeeklyOffDayConfiguration : IEntityTypeConfiguration<WeeklyOffDay>
{
    public void Configure(EntityTypeBuilder<WeeklyOffDay> builder)
    {
        builder.ToTable("WeeklyOffDays");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.DayOfWeek).HasConversion<int>().IsRequired();
        builder.HasAlternateKey(x => new { x.TenantId, x.Id });
        builder.HasIndex(x => new { x.TenantId, x.WeeklyOffConfigurationId, x.DayOfWeek }).IsUnique();
        builder.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}
