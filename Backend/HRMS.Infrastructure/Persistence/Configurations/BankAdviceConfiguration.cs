using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

public sealed class BankAdviceBatchConfiguration : IEntityTypeConfiguration<BankAdviceBatch>
{
    public void Configure(EntityTypeBuilder<BankAdviceBatch> builder)
    {
        builder.ToTable("BankAdviceBatches");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.BatchNumber).HasMaxLength(100).IsRequired();
        builder.Property(x => x.CurrencyCode).HasMaxLength(10).IsRequired();
        builder.Property(x => x.Status).HasConversion<int>().IsRequired();
        builder.Property(x => x.TotalAmount).HasPrecision(18, 2);
        builder.Property(x => x.Notes).HasMaxLength(1000);
        builder.HasIndex(x => new { x.TenantId, x.BatchNumber }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.PayrollRunId });
        builder.HasIndex(x => new { x.TenantId, x.Status });
        builder.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.PayrollRun).WithMany().HasForeignKey(x => new { x.TenantId, x.PayrollRunId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.PayrollPeriod).WithMany().HasForeignKey(x => new { x.TenantId, x.PayrollPeriodId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.ConcurrencyVersion).IsConcurrencyToken();
    }
}

public sealed class BankAdvicePaymentConfiguration : IEntityTypeConfiguration<BankAdvicePayment>
{
    public void Configure(EntityTypeBuilder<BankAdvicePayment> builder)
    {
        builder.ToTable("BankAdvicePayments");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.EmployeeCode).HasMaxLength(100).IsRequired();
        builder.Property(x => x.EmployeeName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.CurrencyCode).HasMaxLength(10).IsRequired();
        builder.Property(x => x.NetPay).HasPrecision(18, 2);
        builder.Property(x => x.AccountHolderName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.BankName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.MaskedAccountNumber).HasMaxLength(50).IsRequired();
        builder.Property(x => x.IfscCode).HasMaxLength(30);
        builder.Property(x => x.BranchName).HasMaxLength(200);
        builder.Property(x => x.PaymentReference).HasMaxLength(150).IsRequired();
        builder.Property(x => x.ValidationMessage).HasMaxLength(1000);
        builder.Property(x => x.PaymentStatus).HasConversion<int>().IsRequired();
        builder.Property(x => x.ValidationStatus).HasConversion<int>().IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.PaymentReference }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.BankAdviceBatchId, x.PayrollResultId }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.BankAdviceBatchId });
        builder.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Batch).WithMany(x => x.Payments).HasForeignKey(x => new { x.TenantId, x.BankAdviceBatchId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.PayrollResult).WithMany().HasForeignKey(x => new { x.TenantId, x.PayrollResultId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Employee).WithMany().HasForeignKey(x => new { x.TenantId, x.EmployeeId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class BankAdviceHistoryConfiguration : IEntityTypeConfiguration<BankAdviceHistory>
{
    public void Configure(EntityTypeBuilder<BankAdviceHistory> builder)
    {
        builder.ToTable("BankAdviceHistories");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ChangeType).HasConversion<int>().IsRequired();
        builder.Property(x => x.SnapshotJson).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.Message).HasMaxLength(1000);
        builder.HasIndex(x => new { x.TenantId, x.BankAdviceBatchId, x.ChangedAtUtc });
        builder.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Batch).WithMany(x => x.History).HasForeignKey(x => new { x.TenantId, x.BankAdviceBatchId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}
