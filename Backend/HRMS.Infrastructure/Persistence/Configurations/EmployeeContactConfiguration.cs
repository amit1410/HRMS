using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

/// <summary>
/// Contact information for an employee. Configured as 1:1 from the Employee side in
/// <see cref="EmployeeConfiguration"/>.
/// </summary>
public class EmployeeContactConfiguration : IEntityTypeConfiguration<EmployeeContact>
{
    public void Configure(EntityTypeBuilder<EmployeeContact> builder)
    {
        builder.ToTable("EmployeeContacts");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.TenantId).IsRequired();
        builder.Property(e => e.EmployeeId).IsRequired();
        builder.Property(e => e.OfficialEmail).HasMaxLength(256);
        builder.Property(e => e.PersonalEmail).HasMaxLength(256);
        builder.Property(e => e.AlternateEmail).HasMaxLength(256);
        builder.Property(e => e.OfficialPhone).HasMaxLength(30);
        builder.Property(e => e.PersonalPhone).HasMaxLength(30);
        builder.Property(e => e.EmergencyNumber).HasMaxLength(30);

        // Unique per employee — enforced by the 1:1 FK configured in EmployeeConfiguration.
        builder.HasIndex(e => new { e.TenantId, e.EmployeeId }).IsUnique();

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        // Employee FK is configured from the Employee side (EmployeeConfiguration).
    }
}
