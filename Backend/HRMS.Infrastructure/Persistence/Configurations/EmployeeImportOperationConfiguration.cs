using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

public sealed class EmployeeImportOperationConfiguration : IEntityTypeConfiguration<EmployeeImportOperation>
{
    public void Configure(EntityTypeBuilder<EmployeeImportOperation> builder)
    {
        builder.ToTable("EmployeeImportOperations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.ImportBatchId).IsRequired();
        builder.Property(x => x.WorkbookSha256).IsRequired().HasMaxLength(64).IsUnicode(false);
        builder.Property(x => x.ConfirmationTokenHash).IsRequired().HasMaxLength(64).IsUnicode(false);
        builder.Property(x => x.State).HasConversion<int>().IsRequired();
        builder.Property(x => x.InitiatedByUserId).IsRequired();
        builder.Property(x => x.PreparedAtUtc).IsRequired();
        builder.Property(x => x.ClaimLeaseId);
        builder.Property(x => x.ClaimLeaseExpiresAtUtc);
        builder.Property(x => x.SanitizedSummary).HasMaxLength(500);
        builder.Property(x => x.ConcurrencyStamp).IsConcurrencyToken().IsRequired();
        builder.HasAlternateKey(x => new { x.TenantId, x.Id });
        builder.HasIndex(x => new { x.TenantId, x.WorkbookSha256 }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.ConfirmationTokenHash }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.State, x.PreparedAtUtc });
        builder.HasIndex(x => new { x.TenantId, x.State, x.ClaimLeaseExpiresAtUtc });
        builder.HasIndex(x => x.ImportBatchId).IsUnique();
        builder.HasOne<ImportBatch>().WithMany().HasForeignKey(x => new { x.TenantId, x.ImportBatchId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => new { x.TenantId, x.InitiatedByUserId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Rows).WithOne(x => x.Operation)
            .HasForeignKey(x => new { x.TenantId, x.OperationId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);
    }
}
