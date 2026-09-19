using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

public sealed class PayrollResultConfiguration : IEntityTypeConfiguration<PayrollResult>
{
    public void Configure(EntityTypeBuilder<PayrollResult> b)
    {
        b.ToTable("PayrollResults"); b.HasKey(x => x.Id);
        b.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired(); b.Property(x => x.GrossEarnings).HasPrecision(18, 6); b.Property(x => x.TotalDeductions).HasPrecision(18, 6); b.Property(x => x.NetPay).HasPrecision(18, 6); b.Property(x => x.EmployerContributions).HasPrecision(18, 6); b.Property(x => x.ProrationFactor).HasPrecision(18, 8); b.Property(x => x.Status).HasConversion<int>(); b.Property(x => x.ConcurrencyVersion).HasDefaultValue(1).IsConcurrencyToken();
        b.HasIndex(x => new { x.TenantId, x.PayrollRunId }); b.HasIndex(x => new { x.TenantId, x.PayrollRunId, x.EmployeeId, x.CalculationAttemptId }).IsUnique(); b.HasIndex(x => new { x.TenantId, x.PayrollRunId, x.EmployeeId, x.IsCurrent });
        b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.PayrollRun).WithMany().HasForeignKey(new[] { "TenantId", "PayrollRunId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.PayrollRunEmployee).WithMany().HasForeignKey(new[] { "TenantId", "PayrollRunEmployeeId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Employee).WithMany().HasForeignKey(new[] { "TenantId", "EmployeeId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.EmployeeSalaryAssignment).WithMany().HasForeignKey(new[] { "TenantId", "EmployeeSalaryAssignmentId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PayrollResultComponentConfiguration : IEntityTypeConfiguration<PayrollResultComponent>
{
    public void Configure(EntityTypeBuilder<PayrollResultComponent> b)
    {
        b.ToTable("PayrollResultComponents"); b.HasKey(x => x.Id); b.Property(x => x.ComponentCode).HasMaxLength(50).IsRequired(); b.Property(x => x.ComponentName).HasMaxLength(150).IsRequired(); b.Property(x => x.ComponentType).HasConversion<int>(); b.Property(x => x.CalculationType).HasConversion<int>(); b.Property(x => x.BaseAmount).HasPrecision(18, 6); b.Property(x => x.Rate).HasPrecision(18, 6); b.Property(x => x.Quantity).HasPrecision(18, 6); b.Property(x => x.UnproratedAmount).HasPrecision(18, 6); b.Property(x => x.ProrationFactor).HasPrecision(18, 8); b.Property(x => x.CalculatedAmount).HasPrecision(18, 6); b.Property(x => x.CalculationSource).HasMaxLength(80).IsRequired(); b.Property(x => x.FormulaSnapshot).HasMaxLength(2000); b.Property(x => x.CalculationMetadata).HasMaxLength(4000);
        b.HasIndex(x => new { x.TenantId, x.PayrollResultId }); b.HasIndex(x => new { x.TenantId, x.SalaryComponentId }); b.HasIndex(x => new { x.TenantId, x.CalculationAttemptId }); b.HasOne(x => x.PayrollResult).WithMany(x => x.Components).HasForeignKey(new[] { "TenantId", "PayrollResultId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade); b.HasOne(x => x.SalaryComponent).WithMany().HasForeignKey(new[] { "TenantId", "SalaryComponentId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PayrollCalculationErrorConfiguration : IEntityTypeConfiguration<PayrollCalculationError>
{
    public void Configure(EntityTypeBuilder<PayrollCalculationError> b)
    {
        b.ToTable("PayrollCalculationErrors"); b.HasKey(x => x.Id); b.Property(x => x.ErrorCode).HasConversion<int>(); b.Property(x => x.Message).HasMaxLength(2000).IsRequired(); b.HasIndex(x => new { x.TenantId, x.PayrollRunId }); b.HasIndex(x => new { x.TenantId, x.EmployeeId }); b.HasIndex(x => new { x.TenantId, x.PayrollRunId, x.CalculationAttemptId, x.IsCurrent }); b.HasOne(x => x.PayrollRun).WithMany().HasForeignKey(new[] { "TenantId", "PayrollRunId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade); b.HasOne(x => x.PayrollRunEmployee).WithMany().HasForeignKey(new[] { "TenantId", "PayrollRunEmployeeId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.Employee).WithMany().HasForeignKey(new[] { "TenantId", "EmployeeId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.SalaryComponent).WithMany().HasForeignKey(new[] { "TenantId", "SalaryComponentId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PayrollCalculationHistoryConfiguration : IEntityTypeConfiguration<PayrollCalculationHistory>
{
    public void Configure(EntityTypeBuilder<PayrollCalculationHistory> b)
    {
        b.ToTable("PayrollCalculationHistories"); b.HasKey(x => x.Id); b.Property(x => x.ChangeType).HasConversion<int>(); b.Property(x => x.Message).HasMaxLength(2000); b.HasIndex(x => new { x.TenantId, x.PayrollRunId, x.ChangedAtUtc }); b.HasIndex(x => new { x.TenantId, x.PayrollRunId, x.CalculationAttemptId }); b.HasOne(x => x.PayrollRun).WithMany().HasForeignKey(new[] { "TenantId", "PayrollRunId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade); b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}
