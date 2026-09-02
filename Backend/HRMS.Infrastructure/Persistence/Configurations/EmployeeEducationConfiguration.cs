using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

public class EmployeeEducationConfiguration : IEntityTypeConfiguration<EmployeeEducation>
{
    public void Configure(EntityTypeBuilder<EmployeeEducation> builder)
    {
        builder.ToTable("EmployeeEducationRecords");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.TenantId).IsRequired();
        builder.Property(e => e.EmployeeId).IsRequired();
        builder.Property(e => e.EducationLevel).IsRequired().HasMaxLength(100);
        builder.Property(e => e.Qualification).IsRequired().HasMaxLength(200);
        builder.Property(e => e.University).HasMaxLength(200);
        builder.Property(e => e.Institute).HasMaxLength(200);
        builder.Property(e => e.EducationType).HasConversion<int>().IsRequired();
        builder.Property(e => e.AreaOfSpecialization).HasMaxLength(200);
        builder.Property(e => e.Score).HasMaxLength(50);
        builder.Property(e => e.DocumentOfProof).HasMaxLength(500);

        builder.HasIndex(e => new { e.TenantId, e.EmployeeId });

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Employee)
            .WithMany(e => e.EducationRecords)
            .HasForeignKey(e => new { e.TenantId, e.EmployeeId })
            .HasPrincipalKey(e => new { e.TenantId, e.Id })
            .OnDelete(DeleteBehavior.Cascade);
    }
}
