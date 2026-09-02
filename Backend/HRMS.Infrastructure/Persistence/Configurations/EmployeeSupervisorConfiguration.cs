using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

/// <summary>
/// Supervisor hierarchy for an employee. Configured as 1:1 from the Employee side in
/// <see cref="EmployeeConfiguration"/>.
/// </summary>
public class EmployeeSupervisorConfiguration : IEntityTypeConfiguration<EmployeeSupervisor>
{
    public void Configure(EntityTypeBuilder<EmployeeSupervisor> builder)
    {
        builder.ToTable("EmployeeSupervisors");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.TenantId).IsRequired();
        builder.Property(e => e.EmployeeId).IsRequired();
        builder.Property(e => e.L1ManagerCode).HasMaxLength(20);
        builder.Property(e => e.L1ManagerName).HasMaxLength(200);
        builder.Property(e => e.L2ManagerCode).HasMaxLength(20);
        builder.Property(e => e.L2ManagerName).HasMaxLength(200);
        builder.Property(e => e.L3ManagerCode).HasMaxLength(20);
        builder.Property(e => e.L3ManagerName).HasMaxLength(200);
        builder.Property(e => e.L4ManagerCode).HasMaxLength(20);
        builder.Property(e => e.L4ManagerName).HasMaxLength(200);
        builder.Property(e => e.L5ManagerCode).HasMaxLength(20);
        builder.Property(e => e.L5ManagerName).HasMaxLength(200);
        builder.Property(e => e.TimeManagerCode).HasMaxLength(20);
        builder.Property(e => e.TimeManagerName).HasMaxLength(200);
        builder.Property(e => e.EroCode).HasMaxLength(20);
        builder.Property(e => e.EroName).HasMaxLength(200);
        builder.Property(e => e.ChroManagerCode).HasMaxLength(20);
        builder.Property(e => e.ChroManagerName).HasMaxLength(200);

        // Unique per employee — enforced by the 1:1 FK configured in EmployeeConfiguration.
        builder.HasIndex(e => new { e.TenantId, e.EmployeeId }).IsUnique();

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        // Employee FK is configured from the Employee side (EmployeeConfiguration).
    }
}
