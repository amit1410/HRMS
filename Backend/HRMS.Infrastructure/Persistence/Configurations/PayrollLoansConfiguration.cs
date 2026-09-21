using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

internal static class PayrollLoansMapping
{
    public static void Tenant<TEntity>(EntityTypeBuilder<TEntity> b) where TEntity : class, HRMS.Domain.Common.ITenantEntity =>
        b.HasOne<Tenant>("Tenant").WithMany().HasForeignKey(nameof(HRMS.Domain.Common.ITenantEntity.TenantId)).OnDelete(DeleteBehavior.Restrict);
}

public sealed class LoanProductConfiguration : IEntityTypeConfiguration<LoanProduct>
{
    public void Configure(EntityTypeBuilder<LoanProduct> b)
    {
        b.ToTable("LoanProducts"); b.HasKey(x => x.Id); b.Property(x => x.Code).HasMaxLength(50).IsRequired(); b.Property(x => x.Name).HasMaxLength(200).IsRequired(); b.Property(x => x.Description).HasMaxLength(1000); b.Property(x => x.ProductType).HasConversion<int>(); b.Property(x => x.InterestMethod).HasConversion<int>(); b.Property(x => x.InterestRateType).HasConversion<int>(); b.Property(x => x.RecoveryPolicy).HasConversion<int>(); b.Property(x => x.CurrencyCode).HasMaxLength(10).IsRequired(); b.Property(x => x.MinAmount).HasPrecision(18, 2); b.Property(x => x.MaxAmount).HasPrecision(18, 2); b.Property(x => x.InterestRate).HasPrecision(9, 4); b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique(); PayrollLoansMapping.Tenant(b);
    }
}

public sealed class LoanProductVersionConfiguration : IEntityTypeConfiguration<LoanProductVersion>
{
    public void Configure(EntityTypeBuilder<LoanProductVersion> b)
    {
        b.ToTable("LoanProductVersions"); b.HasKey(x => x.Id); b.Property(x => x.InterestMethod).HasConversion<int>(); b.Property(x => x.Status).HasConversion<int>(); b.Property(x => x.RecoveryPolicy).HasConversion<int>(); b.Property(x => x.MinAmount).HasPrecision(18, 2); b.Property(x => x.MaxAmount).HasPrecision(18, 2); b.Property(x => x.InterestRate).HasPrecision(9, 4); b.HasIndex(x => new { x.TenantId, x.LoanProductId, x.EffectiveFrom }); b.HasOne(x => x.LoanProduct).WithMany(x => x.Versions).HasForeignKey(new[] { "TenantId", "LoanProductId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); PayrollLoansMapping.Tenant(b);
    }
}

public sealed class EmployeeLoanConfiguration : IEntityTypeConfiguration<EmployeeLoan>
{
    public void Configure(EntityTypeBuilder<EmployeeLoan> b)
    {
        b.ToTable("EmployeeLoans"); b.HasKey(x => x.Id); b.Property(x => x.LoanNumber).HasMaxLength(50).IsRequired(); b.Property(x => x.CurrencyCode).HasMaxLength(10).IsRequired(); b.Property(x => x.Status).HasConversion<int>(); b.Property(x => x.InterestMethod).HasConversion<int>(); b.Property(x => x.RecoveryPolicy).HasConversion<int>(); b.Property(x => x.InterestRate).HasPrecision(9, 4); b.Property(x => x.RequestedAmount).HasPrecision(18, 2); b.Property(x => x.ApprovedAmount).HasPrecision(18, 2); b.Property(x => x.DisbursedAmount).HasPrecision(18, 2); b.Property(x => x.OutstandingPrincipal).HasPrecision(18, 2); b.Property(x => x.OutstandingInterest).HasPrecision(18, 2); b.Property(x => x.OutstandingTotal).HasPrecision(18, 2); b.HasIndex(x => new { x.TenantId, x.LoanNumber }).IsUnique(); b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.Status }); b.Property(x => x.ConcurrencyVersion).IsConcurrencyToken().HasDefaultValue(1); b.HasOne(x => x.Employee).WithMany().HasForeignKey(new[] { "TenantId", "EmployeeId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.LoanProduct).WithMany(x => x.Loans).HasForeignKey(new[] { "TenantId", "LoanProductId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.LoanProductVersion).WithMany(x => x.Loans).HasForeignKey(new[] { "TenantId", "LoanProductVersionId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); PayrollLoansMapping.Tenant(b);
    }
}

public sealed class LoanInstallmentConfiguration : IEntityTypeConfiguration<LoanInstallment>
{
    public void Configure(EntityTypeBuilder<LoanInstallment> b)
    {
        b.ToTable("LoanInstallments"); b.HasKey(x => x.Id); b.Property(x => x.Status).HasConversion<int>(); foreach (var p in new[] { nameof(LoanInstallment.OpeningPrincipal), nameof(LoanInstallment.PrincipalAmount), nameof(LoanInstallment.InterestAmount), nameof(LoanInstallment.InstallmentAmount), nameof(LoanInstallment.ClosingPrincipal), nameof(LoanInstallment.RecoveredAmount) }) b.Property<decimal>(p).HasPrecision(18, 2); b.HasIndex(x => new { x.TenantId, x.EmployeeLoanId, x.InstallmentNumber }).IsUnique(); b.HasIndex(x => new { x.TenantId, x.Status, x.DueDate }); b.HasOne(x => x.EmployeeLoan).WithMany(x => x.Installments).HasForeignKey(new[] { "TenantId", "EmployeeLoanId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade); b.HasOne(x => x.PayrollRun).WithMany().HasForeignKey(new[] { "TenantId", "PayrollRunId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.PayrollResult).WithMany().HasForeignKey(new[] { "TenantId", "PayrollResultId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); PayrollLoansMapping.Tenant(b);
    }
}

public sealed class LoanRepaymentConfiguration : IEntityTypeConfiguration<LoanRepayment>
{
    public void Configure(EntityTypeBuilder<LoanRepayment> b)
    {
        b.ToTable("LoanRepayments"); b.HasKey(x => x.Id); b.Property(x => x.RepaymentType).HasConversion<int>(); b.Property(x => x.SourceType).HasMaxLength(100).IsRequired(); b.Property(x => x.Reference).HasMaxLength(200); foreach (var p in new[] { nameof(LoanRepayment.Amount), nameof(LoanRepayment.PrincipalAmount), nameof(LoanRepayment.InterestAmount) }) b.Property<decimal>(p).HasPrecision(18, 2); b.HasIndex(x => new { x.TenantId, x.EmployeeLoanId, x.RepaymentType }); b.HasIndex(x => new { x.TenantId, x.LoanInstallmentId }).IsUnique().HasFilter("[LoanInstallmentId] IS NOT NULL"); b.HasOne(x => x.EmployeeLoan).WithMany(x => x.Repayments).HasForeignKey(new[] { "TenantId", "EmployeeLoanId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade); b.HasOne(x => x.LoanInstallment).WithMany().HasForeignKey(new[] { "TenantId", "LoanInstallmentId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); PayrollLoansMapping.Tenant(b);
    }
}

public sealed class LoanHistoryConfiguration : IEntityTypeConfiguration<LoanHistory>
{
    public void Configure(EntityTypeBuilder<LoanHistory> b)
    {
        b.ToTable("LoanHistories"); b.HasKey(x => x.Id); b.Property(x => x.EventType).HasConversion<int>(); b.Property(x => x.PreviousStatus).HasConversion<int>(); b.Property(x => x.NewStatus).HasConversion<int>(); b.Property(x => x.SourceType).HasMaxLength(100); b.Property(x => x.Reason).HasMaxLength(1000); b.Property(x => x.Amount).HasPrecision(18, 2); b.HasIndex(x => new { x.TenantId, x.EmployeeLoanId, x.OccurredAtUtc }); b.HasOne(x => x.EmployeeLoan).WithMany(x => x.History).HasForeignKey(new[] { "TenantId", "EmployeeLoanId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade); PayrollLoansMapping.Tenant(b);
    }
}
