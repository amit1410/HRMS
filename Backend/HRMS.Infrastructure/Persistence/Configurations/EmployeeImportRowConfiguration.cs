using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

public sealed class EmployeeImportRowConfiguration : IEntityTypeConfiguration<EmployeeImportRow>
{
    public void Configure(EntityTypeBuilder<EmployeeImportRow> builder)
    {
        builder.ToTable("EmployeeImportRows");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.OperationId).IsRequired();
        builder.Property(x => x.ExcelRowNumber).IsRequired();
        builder.Property(x => x.Status).HasConversion<int>().IsRequired().IsConcurrencyToken();
        builder.Property(x => x.EmployeeCode).HasMaxLength(100);
        builder.Property(x => x.ErrorCode).HasMaxLength(100);
        builder.Property(x => x.SanitizedErrorMessage).HasMaxLength(500);
        builder.HasAlternateKey(x => new { x.TenantId, x.Id });
        builder.HasIndex(x => new { x.TenantId, x.OperationId, x.ExcelRowNumber }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.OperationId, x.Status });
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}
