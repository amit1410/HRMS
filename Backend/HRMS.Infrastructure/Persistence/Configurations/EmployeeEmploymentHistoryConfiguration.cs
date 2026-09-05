using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

public class EmployeeEmploymentHistoryConfiguration : IEntityTypeConfiguration<EmployeeEmploymentHistory>
{
    public void Configure(EntityTypeBuilder<EmployeeEmploymentHistory> builder)
    {
        builder.ToTable("EmployeeEmploymentHistory");
        builder.HasKey(e => e.Id);

        // Requests retain the effective history row that authorized their policy decision.
        // The tenant and employee are part of the principal key so that this historical
        // reference cannot cross either ownership boundary.
        builder.HasAlternateKey(e => new { e.TenantId, e.EmployeeId, e.Id });

        builder.Property(e => e.TenantId).IsRequired();
        builder.Property(e => e.EmployeeId).IsRequired();
        builder.Property(e => e.EffectiveFrom).IsRequired();

        // Snapshot string fields
        builder.Property(e => e.BusinessRole).HasMaxLength(200);
        builder.Property(e => e.GradeLevel).HasMaxLength(50);
        builder.Property(e => e.CareerGroup).HasMaxLength(100);
        builder.Property(e => e.DesignationName).HasMaxLength(200);
        builder.Property(e => e.DepartmentName).HasMaxLength(200);
        builder.Property(e => e.ManagerCode).HasMaxLength(20);
        builder.Property(e => e.ManagerName).HasMaxLength(200);
        builder.Property(e => e.ChangeReasonDescription).HasMaxLength(500);
        builder.Property(e => e.CreatedBy).HasMaxLength(256);

        // Enum conversions
        builder.Property(e => e.EmploymentType).HasConversion<int>().IsRequired();
        builder.Property(e => e.EmploymentStatus).HasConversion<int>().IsRequired();
        builder.Property(e => e.ChangeReason).HasConversion<int>().IsRequired();

        // Primary query patterns
        builder.HasIndex(e => new { e.TenantId, e.EmployeeId, e.EffectiveFrom });
        builder.HasIndex(e => new { e.TenantId, e.EmployeeId, e.EffectiveTo });

        // Tenant
        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        // Employee — Restrict: preserving employment history when an employee is deleted.
        // SetNull on ManagerId already references Employees, so we can't have two cascade paths.
        builder.HasOne(e => e.Employee)
            .WithMany(e => e.EmploymentHistory)
            .HasForeignKey(e => new { e.TenantId, e.EmployeeId })
            .HasPrincipalKey(e => new { e.TenantId, e.Id })
            .OnDelete(DeleteBehavior.Restrict);

        // --- Organizational FK references (all SetNull to preserve history) ---
        // Using simple FKs (not composite) so SetNull works — tenant isolation
        // is enforced by application validation and global query filters.

        builder.HasOne(e => e.HoldingCompany)
            .WithMany()
            .HasForeignKey(e => e.HoldingCompanyId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.Lob)
            .WithMany()
            .HasForeignKey(e => e.LobId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.Organisation)
            .WithMany()
            .HasForeignKey(e => e.OrganisationId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.Department)
            .WithMany()
            .HasForeignKey(e => e.DepartmentId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.SubDepartment)
            .WithMany()
            .HasForeignKey(e => e.SubDepartmentId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.Section)
            .WithMany()
            .HasForeignKey(e => e.SectionId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.SubSection)
            .WithMany()
            .HasForeignKey(e => e.SubSectionId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.Function)
            .WithMany()
            .HasForeignKey(e => e.FunctionId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.SubFunction)
            .WithMany()
            .HasForeignKey(e => e.SubFunctionId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.Grade)
            .WithMany()
            .HasForeignKey(e => e.GradeId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.Designation)
            .WithMany()
            .HasForeignKey(e => e.DesignationId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.EmployeeType)
            .WithMany()
            .HasForeignKey(e => e.EmployeeTypeId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.CountryLocation)
            .WithMany()
            .HasForeignKey(e => e.CountryLocationId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.WorkLocation)
            .WithMany()
            .HasForeignKey(e => e.WorkLocationId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.CostCenter)
            .WithMany()
            .HasForeignKey(e => e.CostCenterId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.Manager)
            .WithMany()
            .HasForeignKey(e => e.ManagerId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.PositionChangeReason)
            .WithMany()
            .HasForeignKey(e => e.PositionChangeReasonId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
