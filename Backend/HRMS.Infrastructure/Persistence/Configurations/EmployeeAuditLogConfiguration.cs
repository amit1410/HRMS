using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

public class EmployeeAuditLogConfiguration : IEntityTypeConfiguration<EmployeeAuditLog>
{
    public void Configure(EntityTypeBuilder<EmployeeAuditLog> builder)
    {
        builder.ToTable("EmployeeAuditLogs");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.TenantId).IsRequired();
        builder.Property(e => e.EmployeeId).IsRequired();
        builder.Property(e => e.EmployeeCode).HasMaxLength(20);
        builder.Property(e => e.Module).IsRequired().HasMaxLength(100);
        builder.Property(e => e.Section).HasMaxLength(100);
        builder.Property(e => e.EntityName).HasMaxLength(200);
        builder.Property(e => e.FieldName).HasMaxLength(200);
        builder.Property(e => e.OldValue).HasMaxLength(2000);
        builder.Property(e => e.NewValue).HasMaxLength(2000);
        builder.Property(e => e.ChangeType).HasConversion<int>().IsRequired();
        builder.Property(e => e.ChangedBy).IsRequired().HasMaxLength(256);
        builder.Property(e => e.Reason).HasMaxLength(500);
        builder.Property(e => e.Source).HasMaxLength(50);
        builder.Property(e => e.IpAddress).HasMaxLength(50);

        // Primary query patterns
        builder.HasIndex(e => new { e.TenantId, e.EmployeeId, e.CreatedDate });
        builder.HasIndex(e => new { e.TenantId, e.EmployeeId, e.Module });
        builder.HasIndex(e => new { e.TenantId, e.ImportBatchId });

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Employee)
            .WithMany(e => e.AuditLogs)
            .HasForeignKey(e => new { e.TenantId, e.EmployeeId })
            .HasPrincipalKey(e => new { e.TenantId, e.Id })
            .OnDelete(DeleteBehavior.Cascade);
    }
}
