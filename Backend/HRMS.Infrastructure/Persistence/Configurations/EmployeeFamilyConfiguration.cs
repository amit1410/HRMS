using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

public class EmployeeFamilyConfiguration : IEntityTypeConfiguration<EmployeeFamily>
{
    public void Configure(EntityTypeBuilder<EmployeeFamily> builder)
    {
        builder.ToTable("EmployeeFamilyMembers");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.TenantId).IsRequired();
        builder.Property(e => e.EmployeeId).IsRequired();
        builder.Property(e => e.Salutation).HasMaxLength(20);
        builder.Property(e => e.FirstName).IsRequired().HasMaxLength(100);
        builder.Property(e => e.MiddleName).HasMaxLength(100);
        builder.Property(e => e.LastName).IsRequired().HasMaxLength(100);
        builder.Property(e => e.Relationship).IsRequired().HasMaxLength(50);
        builder.Property(e => e.Gender).HasConversion<int>().IsRequired();
        builder.Property(e => e.BloodGroup).HasConversion<int>().IsRequired();
        builder.Property(e => e.Nationality).HasMaxLength(100);
        builder.Property(e => e.Occupation).HasMaxLength(200);
        builder.Property(e => e.NomineePercentage).HasPrecision(5, 2);

        builder.HasIndex(e => new { e.TenantId, e.EmployeeId });

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Employee)
            .WithMany(e => e.FamilyMembers)
            .HasForeignKey(e => new { e.TenantId, e.EmployeeId })
            .HasPrincipalKey(e => new { e.TenantId, e.Id })
            .OnDelete(DeleteBehavior.Cascade);
    }
}
