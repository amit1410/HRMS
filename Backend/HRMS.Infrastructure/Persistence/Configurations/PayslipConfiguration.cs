using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

public sealed class PayslipConfiguration : IEntityTypeConfiguration<Payslip>
{
    public void Configure(EntityTypeBuilder<Payslip> b)
    {
        b.ToTable("Payslips"); b.HasKey(x => x.Id);
        b.Property(x => x.PayslipNumber).HasMaxLength(80).IsRequired(); b.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
        b.Property(x => x.EmployeeCode).HasMaxLength(80).IsRequired(); b.Property(x => x.EmployeeName).HasMaxLength(240).IsRequired();
        b.Property(x => x.Designation).HasMaxLength(160); b.Property(x => x.Department).HasMaxLength(160); b.Property(x => x.WorkLocation).HasMaxLength(160); b.Property(x => x.SalaryStructureReference).HasMaxLength(180);
        b.Property(x => x.GrossEarnings).HasPrecision(18, 6); b.Property(x => x.TotalDeductions).HasPrecision(18, 6); b.Property(x => x.NetPay).HasPrecision(18, 6); b.Property(x => x.EmployerContributionTotal).HasPrecision(18, 6); b.Property(x => x.Status).HasConversion<int>(); b.Property(x => x.ConcurrencyVersion).HasDefaultValue(1);
        b.HasIndex(x => new { x.TenantId, x.PayslipNumber }).IsUnique(); b.HasIndex(x => new { x.TenantId, x.PayrollRunId }); b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.PeriodEndDate }); b.HasIndex(x => new { x.TenantId, x.PayrollResultId });
        b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.PayrollRun).WithMany().HasForeignKey(new[] { "TenantId", "PayrollRunId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.PayrollResult).WithMany().HasForeignKey(new[] { "TenantId", "PayrollResultId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.PayrollRunEmployee).WithMany().HasForeignKey(new[] { "TenantId", "PayrollRunEmployeeId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Employee).WithMany().HasForeignKey(new[] { "TenantId", "EmployeeId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PayslipLineConfiguration : IEntityTypeConfiguration<PayslipLine>
{
    public void Configure(EntityTypeBuilder<PayslipLine> b)
    {
        b.ToTable("PayslipLines"); b.HasKey(x => x.Id); b.Property(x => x.ComponentCode).HasMaxLength(80).IsRequired(); b.Property(x => x.ComponentName).HasMaxLength(180).IsRequired(); b.Property(x => x.ComponentType).HasMaxLength(50).IsRequired(); b.Property(x => x.DisplayGroup).HasMaxLength(80).IsRequired(); b.Property(x => x.Source).HasMaxLength(80).IsRequired(); b.Property(x => x.Amount).HasPrecision(18, 6); b.Property(x => x.EmployerAmount).HasPrecision(18, 6);
        b.HasIndex(x => new { x.TenantId, x.PayslipId, x.Sequence }); b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.Payslip).WithMany(x => x.Lines).HasForeignKey(new[] { "TenantId", "PayslipId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class PayslipHistoryConfiguration : IEntityTypeConfiguration<PayslipHistory>
{
    public void Configure(EntityTypeBuilder<PayslipHistory> b)
    {
        b.ToTable("PayslipHistories"); b.HasKey(x => x.Id); b.Property(x => x.ChangeType).HasConversion<int>(); b.Property(x => x.Message).HasMaxLength(500); b.HasIndex(x => new { x.TenantId, x.PayslipId, x.ChangedAtUtc }); b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.Payslip).WithMany(x => x.History).HasForeignKey(new[] { "TenantId", "PayslipId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.ActorUser).WithMany().HasForeignKey(new[] { "TenantId", "ActorUserId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.NoAction);
    }
}
