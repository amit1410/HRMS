using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

/// <summary>
/// Additional info for an employee. Configured as 1:1 from the Employee side in
/// <see cref="EmployeeConfiguration"/>.
/// </summary>
public class EmployeeAdditionalInfoConfiguration : IEntityTypeConfiguration<EmployeeAdditionalInfo>
{
    public void Configure(EntityTypeBuilder<EmployeeAdditionalInfo> builder)
    {
        builder.ToTable("EmployeeAdditionalInfo");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.TenantId).IsRequired();
        builder.Property(e => e.EmployeeId).IsRequired();
        builder.Property(e => e.Division).HasMaxLength(200);
        builder.Property(e => e.PaPsa).HasMaxLength(100);
        builder.Property(e => e.AdditionalEmployeeCode).HasMaxLength(50);
        builder.Property(e => e.ContractId).HasMaxLength(100);

        // Unique per employee — enforced by the 1:1 FK configured in EmployeeConfiguration.
        builder.HasIndex(e => new { e.TenantId, e.EmployeeId }).IsUnique();

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        // Employee FK is configured from the Employee side (EmployeeConfiguration).
    }
}
