using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

public class EmployeePreviousEmploymentConfiguration : IEntityTypeConfiguration<EmployeePreviousEmployment>
{
    public void Configure(EntityTypeBuilder<EmployeePreviousEmployment> builder)
    {
        builder.ToTable("EmployeePreviousEmployments");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.TenantId).IsRequired();
        builder.Property(e => e.EmployeeId).IsRequired();
        builder.Property(e => e.Company).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Designation).HasMaxLength(200);
        builder.Property(e => e.Location).HasMaxLength(200);
        builder.Property(e => e.EmploymentType).HasConversion<int>().IsRequired();
        builder.Property(e => e.DocumentOfProof).HasMaxLength(500);

        builder.HasIndex(e => new { e.TenantId, e.EmployeeId });

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Employee)
            .WithMany(e => e.PreviousEmployments)
            .HasForeignKey(e => new { e.TenantId, e.EmployeeId })
            .HasPrincipalKey(e => new { e.TenantId, e.Id })
            .OnDelete(DeleteBehavior.Cascade);
    }
}
