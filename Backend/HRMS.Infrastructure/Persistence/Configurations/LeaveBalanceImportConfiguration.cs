using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

public sealed class LeaveBalanceImportBatchConfiguration : IEntityTypeConfiguration<LeaveBalanceImportBatch>
{
    public void Configure(EntityTypeBuilder<LeaveBalanceImportBatch> b)
    {
        b.ToTable("LeaveBalanceImportBatches");
        b.HasKey(x => x.Id);
        b.Property(x => x.FileName).HasMaxLength(500).IsRequired();
        b.Property(x => x.Status).HasConversion<int>().IsRequired();
        b.Property(x => x.FailureReason).HasMaxLength(2000);
        b.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
        b.HasIndex(x => new { x.TenantId, x.UploadedAtUtc });
        b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.UploadedByUser).WithMany().HasForeignKey(x => new { x.TenantId, x.UploadedByUserId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class LeaveBalanceImportRowConfiguration : IEntityTypeConfiguration<LeaveBalanceImportRow>
{
    public void Configure(EntityTypeBuilder<LeaveBalanceImportRow> b)
    {
        b.ToTable("LeaveBalanceImportRows");
        b.HasKey(x => x.Id);
        b.Property(x => x.EmployeeCode).HasMaxLength(100).IsRequired();
        b.Property(x => x.LeaveTypeCode).HasMaxLength(100).IsRequired();
        b.Property(x => x.LeavePeriod).HasMaxLength(100).IsRequired();
        b.Property(x => x.OpeningBalanceText).HasMaxLength(100).IsRequired();
        b.Property(x => x.OpeningBalance).HasPrecision(9, 3);
        b.Property(x => x.EffectiveDateText).HasMaxLength(50).IsRequired();
        b.Property(x => x.Remarks).HasMaxLength(1000);
        b.Property(x => x.Status).HasConversion<int>().IsRequired();
        b.Property(x => x.ErrorCode).HasMaxLength(100);
        b.Property(x => x.ErrorMessage).HasMaxLength(2000);
        b.Property(x => x.IdempotencyKey).HasMaxLength(200);
        b.HasIndex(x => new { x.TenantId, x.BatchId, x.RowNumber }).IsUnique();
        b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Batch).WithMany(x => x.Rows).HasForeignKey(x => new { x.TenantId, x.BatchId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Employee).WithMany().HasForeignKey(x => new { x.TenantId, x.EmployeeId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.LeaveType).WithMany().HasForeignKey(x => new { x.TenantId, x.LeaveTypeId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.LeavePeriodEntity).WithMany().HasForeignKey(x => new { x.TenantId, x.LeavePeriodId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
