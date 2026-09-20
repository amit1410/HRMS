using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

public sealed class PayrollCompliancePeriodConfiguration : IEntityTypeConfiguration<PayrollCompliancePeriod>
{
    public void Configure(EntityTypeBuilder<PayrollCompliancePeriod> b)
    {
        b.ToTable("PayrollCompliancePeriods"); b.HasKey(x => x.Id); b.Property(x => x.JurisdictionCode).HasMaxLength(20).IsRequired(); b.Property(x => x.ComplianceType).HasConversion<int>(); b.Property(x => x.Status).HasConversion<int>();
        b.HasIndex(x => new { x.TenantId, x.JurisdictionCode, x.ComplianceType, x.PeriodStart, x.PeriodEnd }).IsUnique();
        b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PayrollStatutoryReturnBatchConfiguration : IEntityTypeConfiguration<PayrollStatutoryReturnBatch>
{
    public void Configure(EntityTypeBuilder<PayrollStatutoryReturnBatch> b)
    {
        b.ToTable("PayrollStatutoryReturnBatches"); b.HasKey(x => x.Id); b.Property(x => x.ComplianceType).HasConversion<int>(); b.Property(x => x.Status).HasConversion<int>(); b.Property(x => x.JurisdictionCode).HasMaxLength(20).IsRequired(); b.Property(x => x.BatchNumber).HasMaxLength(100).IsRequired(); b.Property(x => x.ConcurrencyVersion).HasDefaultValue(1).IsConcurrencyToken();
        foreach (var p in new[] { nameof(PayrollStatutoryReturnBatch.GrossRelevantWages), nameof(PayrollStatutoryReturnBatch.EmployeeContribution), nameof(PayrollStatutoryReturnBatch.EmployerContribution), nameof(PayrollStatutoryReturnBatch.TotalDeduction), nameof(PayrollStatutoryReturnBatch.TotalPayable) }) b.Property<decimal>(p).HasPrecision(18, 2);
        b.HasIndex(x => new { x.TenantId, x.BatchNumber }).IsUnique(); b.HasIndex(x => new { x.TenantId, x.PayrollCompliancePeriodId }); b.HasIndex(x => new { x.TenantId, x.Status });
        b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.CompliancePeriod).WithMany(x => x.ReturnBatches).HasForeignKey(new[] { "TenantId", "PayrollCompliancePeriodId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PayrollStatutoryReturnEmployeeConfiguration : IEntityTypeConfiguration<PayrollStatutoryReturnEmployee>
{
    public void Configure(EntityTypeBuilder<PayrollStatutoryReturnEmployee> b)
    {
        b.ToTable("PayrollStatutoryReturnEmployees"); b.HasKey(x => x.Id); b.Property(x => x.ValidationStatus).HasConversion<int>(); b.Property(x => x.EmployeeCodeSnapshot).HasMaxLength(50).IsRequired(); b.Property(x => x.EmployeeNameSnapshot).HasMaxLength(200).IsRequired(); b.Property(x => x.Uan).HasMaxLength(50); b.Property(x => x.EsicNumber).HasMaxLength(50); b.Property(x => x.Pan).HasMaxLength(20); b.Property(x => x.PtRegistrationReference).HasMaxLength(100); b.Property(x => x.ValidationMessage).HasMaxLength(1000);
        foreach (var p in new[] { nameof(PayrollStatutoryReturnEmployee.GrossWages), nameof(PayrollStatutoryReturnEmployee.StatutoryWages), nameof(PayrollStatutoryReturnEmployee.EmployeeContribution), nameof(PayrollStatutoryReturnEmployee.EmployerContribution), nameof(PayrollStatutoryReturnEmployee.DeductionAmount), nameof(PayrollStatutoryReturnEmployee.PayableAmount) }) b.Property<decimal>(p).HasPrecision(18, 2);
        b.HasIndex(x => new { x.TenantId, x.PayrollStatutoryReturnBatchId, x.Sequence }); b.HasIndex(x => new { x.TenantId, x.EmployeeId });
        b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.ReturnBatch).WithMany(x => x.Employees).HasForeignKey(new[] { "TenantId", "PayrollStatutoryReturnBatchId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade); b.HasOne(x => x.Employee).WithMany().HasForeignKey(new[] { "TenantId", "EmployeeId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PayrollStatutoryReturnSourceConfiguration : IEntityTypeConfiguration<PayrollStatutoryReturnSource>
{
    public void Configure(EntityTypeBuilder<PayrollStatutoryReturnSource> b)
    {
        b.ToTable("PayrollStatutoryReturnSources"); b.HasKey(x => x.Id); b.Property(x => x.SourceType).HasMaxLength(100).IsRequired(); b.Property(x => x.Amount).HasPrecision(18, 2); b.HasIndex(x => new { x.TenantId, x.PayrollStatutoryResultId }).IsUnique();
        b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.ReturnBatch).WithMany(x => x.Sources).HasForeignKey(new[] { "TenantId", "PayrollStatutoryReturnBatchId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade); b.HasOne(x => x.ReturnEmployee).WithMany().HasForeignKey(new[] { "TenantId", "PayrollStatutoryReturnEmployeeId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PayrollStatutoryComplianceHistoryConfiguration : IEntityTypeConfiguration<PayrollStatutoryComplianceHistory>
{
    public void Configure(EntityTypeBuilder<PayrollStatutoryComplianceHistory> b)
    {
        b.ToTable("PayrollStatutoryComplianceHistories"); b.HasKey(x => x.Id); b.Property(x => x.ChangeType).HasConversion<int>(); b.Property(x => x.Message).HasMaxLength(2000); b.HasIndex(x => new { x.TenantId, x.PayrollStatutoryReturnBatchId, x.ChangedAtUtc });
        b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.ReturnBatch).WithMany(x => x.History).HasForeignKey(new[] { "TenantId", "PayrollStatutoryReturnBatchId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class PayrollStatutoryChallanConfiguration : IEntityTypeConfiguration<PayrollStatutoryChallan>
{
    public void Configure(EntityTypeBuilder<PayrollStatutoryChallan> b)
    {
        b.ToTable("PayrollStatutoryChallans"); b.HasKey(x => x.Id); b.Property(x => x.Status).HasConversion<int>(); b.Property(x => x.ChallanNumber).HasMaxLength(100); b.Property(x => x.BankReference).HasMaxLength(100); b.Property(x => x.ExternalReference).HasMaxLength(100); b.Property(x => x.Amount).HasPrecision(18, 2); b.HasIndex(x => new { x.TenantId, x.PayrollStatutoryReturnBatchId });
        b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.ReturnBatch).WithMany(x => x.Challans).HasForeignKey(new[] { "TenantId", "PayrollStatutoryReturnBatchId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}
