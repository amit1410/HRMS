using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

public class EmployeeEmploymentConfiguration : IEntityTypeConfiguration<EmployeeEmployment>
{
    public void Configure(EntityTypeBuilder<EmployeeEmployment> builder)
    {
        builder.ToTable("EmployeeEmployments");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.TenantId).IsRequired();
        builder.Property(e => e.EmployeeId).IsRequired();
        builder.Property(e => e.FirstHiredDate).IsRequired();
        builder.Property(e => e.DateOfJoining).IsRequired();
        builder.Property(e => e.JobStatus).HasMaxLength(100);
        builder.Property(e => e.ProbationPeriodUnit).HasMaxLength(20);
        builder.Property(e => e.NoticePeriodUnit).HasMaxLength(20);

        // 1:1 with Employee — unique index on TenantId + EmployeeId
        builder.HasIndex(e => new { e.TenantId, e.EmployeeId }).IsUnique();

        builder.HasAlternateKey(e => new { e.TenantId, e.Id });

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        // Employee FK is configured from the Employee side (EmployeeConfiguration)
        // to avoid a convention-detected duplicate FK with Cascade.

        builder.HasOne(e => e.ReferredByEmployee)
            .WithMany()
            .HasForeignKey(e => new { e.TenantId, e.ReferredByEmployeeId })
            .HasPrincipalKey(e => new { e.TenantId, e.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
