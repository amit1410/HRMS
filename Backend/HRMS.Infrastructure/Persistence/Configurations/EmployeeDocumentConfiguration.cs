using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

public class EmployeeDocumentConfiguration : IEntityTypeConfiguration<EmployeeDocument>
{
    public void Configure(EntityTypeBuilder<EmployeeDocument> builder)
    {
        builder.ToTable("EmployeeDocuments");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.TenantId).IsRequired();
        builder.Property(e => e.EmployeeId).IsRequired();
        builder.Property(e => e.PreviousEmploymentId);
        builder.Property(e => e.DocumentName).IsRequired().HasMaxLength(200);
        builder.Property(e => e.DocumentCategory).HasConversion<int>().IsRequired();
        builder.Property(e => e.DocumentNumber).HasMaxLength(100);
        builder.Property(e => e.FilePath).IsRequired().HasMaxLength(1000);
        builder.Property(e => e.ContentType).IsRequired().HasMaxLength(100);
        builder.Property(e => e.UploadedBy).HasMaxLength(256);

        builder.HasIndex(e => new { e.TenantId, e.EmployeeId });
        builder.HasIndex(e => new { e.TenantId, e.PreviousEmploymentId });

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Employee)
            .WithMany(e => e.Documents)
            .HasForeignKey(e => new { e.TenantId, e.EmployeeId })
            .HasPrincipalKey(e => new { e.TenantId, e.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.PreviousEmployment)
            .WithMany(e => e.SupportingDocuments)
            .HasForeignKey(e => new { e.TenantId, e.PreviousEmploymentId })
            .HasPrincipalKey(e => new { e.TenantId, e.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);
    }
}
