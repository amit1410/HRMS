using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

public sealed class EmployeeImportRecoveryAuditConfiguration : IEntityTypeConfiguration<EmployeeImportRecoveryAudit>
{
    public void Configure(EntityTypeBuilder<EmployeeImportRecoveryAudit> builder)
    {
        builder.ToTable("EmployeeImportRecoveryAudits");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.OperationId).IsRequired();
        builder.Property(x => x.RecoveryAtUtc).IsRequired();
        builder.Property(x => x.Operator).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        builder.Property(x => x.Outcome).HasMaxLength(40).IsRequired();
        builder.Property(x => x.SanitizedErrorCode).HasMaxLength(80);
        builder.HasAlternateKey(x => new { x.TenantId, x.Id });
        builder.HasIndex(x => new { x.TenantId, x.OperationId, x.RecoveryAtUtc });
        foreach (var property in builder.Metadata.GetProperties())
            property.SetAfterSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Throw);
        builder.HasOne<EmployeeImportOperation>().WithMany().HasForeignKey(x => new { x.TenantId, x.OperationId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}
