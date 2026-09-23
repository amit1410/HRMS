using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

public sealed class YearEndTaxRunConfiguration : IEntityTypeConfiguration<YearEndTaxRun>
{
    public void Configure(EntityTypeBuilder<YearEndTaxRun> b)
    {
        b.ToTable("YearEndTaxRuns"); b.HasKey(x => x.Id); b.Property(x => x.TaxYearCode).HasMaxLength(40).IsRequired(); b.Property(x => x.Status).HasConversion<int>();
        foreach (var p in new[] { nameof(YearEndTaxRun.TotalTaxDue), nameof(YearEndTaxRun.TotalExcessTax) }) b.Property<decimal>(p).HasPrecision(18, 2);
        b.Property(x => x.ConcurrencyVersion).IsConcurrencyToken(); b.HasIndex(x => new { x.TenantId, x.TaxYear }).IsUnique(); b.HasIndex(x => new { x.TenantId, x.Status });
        b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class YearEndTaxEmployeeConfiguration : IEntityTypeConfiguration<YearEndTaxEmployee>
{
    public void Configure(EntityTypeBuilder<YearEndTaxEmployee> b)
    {
        b.ToTable("YearEndTaxEmployees"); b.HasKey(x => x.Id); b.Property(x => x.Status).HasConversion<int>(); b.Property(x => x.BlockingIssueCode).HasMaxLength(100); b.Property(x => x.BlockingIssueMessage).HasMaxLength(2000); b.Property(x => x.CalculationSnapshotJson).HasMaxLength(8000);
        foreach (var p in new[] { nameof(YearEndTaxEmployee.YtdGross), nameof(YearEndTaxEmployee.YtdTaxableIncome), nameof(YearEndTaxEmployee.YtdTaxDeducted), nameof(YearEndTaxEmployee.ApprovedDeclarationAmount), nameof(YearEndTaxEmployee.ApprovedProofAmount), nameof(YearEndTaxEmployee.PreviousEmployerTaxableIncome), nameof(YearEndTaxEmployee.PreviousEmployerTaxDeducted), nameof(YearEndTaxEmployee.ProjectedRemainingTaxableIncome), nameof(YearEndTaxEmployee.ProjectedAnnualTaxableIncome), nameof(YearEndTaxEmployee.ProjectedAnnualTax), nameof(YearEndTaxEmployee.EstimatedTaxDue), nameof(YearEndTaxEmployee.EstimatedExcessTax), nameof(YearEndTaxEmployee.FinalTaxableIncome), nameof(YearEndTaxEmployee.FinalTaxLiability) }) b.Property<decimal>(p).HasPrecision(18, 2);
        b.Property(x => x.ConcurrencyVersion).IsConcurrencyToken(); b.HasIndex(x => new { x.TenantId, x.RunId, x.EmployeeId }).IsUnique(); b.HasIndex(x => new { x.TenantId, x.RunId, x.Status });
        b.HasOne(x => x.Run).WithMany(x => x.Employees).HasForeignKey(nameof(YearEndTaxEmployee.TenantId), nameof(YearEndTaxEmployee.RunId)).HasPrincipalKey(nameof(YearEndTaxRun.TenantId), nameof(YearEndTaxRun.Id)).OnDelete(DeleteBehavior.Cascade); b.HasOne(x => x.Employee).WithMany().HasForeignKey(nameof(YearEndTaxEmployee.TenantId), nameof(YearEndTaxEmployee.EmployeeId)).HasPrincipalKey(nameof(Employee.TenantId), nameof(Employee.Id)).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class YearEndTaxPreviousEmployerInputConfiguration : IEntityTypeConfiguration<YearEndTaxPreviousEmployerInput>
{
    public void Configure(EntityTypeBuilder<YearEndTaxPreviousEmployerInput> b)
    {
        b.ToTable("YearEndTaxPreviousEmployerInputs"); b.HasKey(x => x.Id); b.Property(x => x.EmployerName).HasMaxLength(200).IsRequired(); b.Property(x => x.EmployerReference).HasMaxLength(200); b.Property(x => x.EvidenceReference).HasMaxLength(500); b.Property(x => x.Status).HasConversion<int>(); b.Property(x => x.ConcurrencyVersion).IsConcurrencyToken();
        foreach (var p in new[] { nameof(YearEndTaxPreviousEmployerInput.TaxableIncome), nameof(YearEndTaxPreviousEmployerInput.TaxDeducted), nameof(YearEndTaxPreviousEmployerInput.EligibleDeductionAmount) }) b.Property<decimal>(p).HasPrecision(18, 2);
        b.HasIndex(x => new { x.TenantId, x.RunId, x.EmployeeId, x.EmployerReference }).IsUnique(); b.HasOne(x => x.Run).WithMany(x => x.PreviousEmployerInputs).HasForeignKey(nameof(YearEndTaxPreviousEmployerInput.TenantId), nameof(YearEndTaxPreviousEmployerInput.RunId)).HasPrincipalKey(nameof(YearEndTaxRun.TenantId), nameof(YearEndTaxRun.Id)).OnDelete(DeleteBehavior.Cascade); b.HasOne(x => x.Employee).WithMany().HasForeignKey(nameof(YearEndTaxPreviousEmployerInput.TenantId), nameof(YearEndTaxPreviousEmployerInput.EmployeeId)).HasPrincipalKey(nameof(Employee.TenantId), nameof(Employee.Id)).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class YearEndTaxAdjustmentConfiguration : IEntityTypeConfiguration<YearEndTaxAdjustment>
{
    public void Configure(EntityTypeBuilder<YearEndTaxAdjustment> b)
    {
        b.ToTable("YearEndTaxAdjustments"); b.HasKey(x => x.Id); b.Property(x => x.Direction).HasConversion<int>(); b.Property(x => x.Status).HasConversion<int>(); b.Property(x => x.Amount).HasPrecision(18, 2); b.Property(x => x.Reason).HasMaxLength(2000).IsRequired(); b.Property(x => x.SourceCalculationReference).HasMaxLength(200).IsRequired(); b.HasIndex(x => new { x.TenantId, x.RunId, x.EmployeeId }).IsUnique();
        b.HasOne(x => x.Run).WithMany(x => x.Adjustments).HasForeignKey(nameof(YearEndTaxAdjustment.TenantId), nameof(YearEndTaxAdjustment.RunId)).HasPrincipalKey(nameof(YearEndTaxRun.TenantId), nameof(YearEndTaxRun.Id)).OnDelete(DeleteBehavior.Cascade); b.HasOne(x => x.Employee).WithMany().HasForeignKey(nameof(YearEndTaxAdjustment.TenantId), nameof(YearEndTaxAdjustment.EmployeeId)).HasPrincipalKey(nameof(Employee.TenantId), nameof(Employee.Id)).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.YearEndTaxEmployee).WithMany(x => x.Adjustments).HasForeignKey(nameof(YearEndTaxAdjustment.TenantId), nameof(YearEndTaxAdjustment.YearEndTaxEmployeeId)).HasPrincipalKey(nameof(YearEndTaxEmployee.TenantId), nameof(YearEndTaxEmployee.Id)).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.PayrollAdjustment).WithMany().HasForeignKey(nameof(YearEndTaxAdjustment.TenantId), nameof(YearEndTaxAdjustment.PayrollAdjustmentId)).HasPrincipalKey(nameof(PayrollAdjustment.TenantId), nameof(PayrollAdjustment.Id)).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class YearEndTaxStatementConfiguration : IEntityTypeConfiguration<YearEndTaxStatement>
{
    public void Configure(EntityTypeBuilder<YearEndTaxStatement> b)
    {
        b.ToTable("YearEndTaxStatements"); b.HasKey(x => x.Id); b.Property(x => x.StatementReference).HasMaxLength(100).IsRequired(); b.Property(x => x.SnapshotJson).HasMaxLength(12000).IsRequired(); b.HasIndex(x => new { x.TenantId, x.RunId, x.EmployeeId }).IsUnique(); b.HasOne(x => x.Run).WithMany().HasForeignKey(nameof(YearEndTaxStatement.TenantId), nameof(YearEndTaxStatement.RunId)).HasPrincipalKey(nameof(YearEndTaxRun.TenantId), nameof(YearEndTaxRun.Id)).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.YearEndTaxEmployee).WithMany(x => x.Statements).HasForeignKey(nameof(YearEndTaxStatement.TenantId), nameof(YearEndTaxStatement.YearEndTaxEmployeeId)).HasPrincipalKey(nameof(YearEndTaxEmployee.TenantId), nameof(YearEndTaxEmployee.Id)).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.Employee).WithMany().HasForeignKey(nameof(YearEndTaxStatement.TenantId), nameof(YearEndTaxStatement.EmployeeId)).HasPrincipalKey(nameof(Employee.TenantId), nameof(Employee.Id)).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class YearEndTaxHistoryConfiguration : IEntityTypeConfiguration<YearEndTaxHistory>
{
    public void Configure(EntityTypeBuilder<YearEndTaxHistory> b)
    {
        b.ToTable("YearEndTaxHistories"); b.HasKey(x => x.Id); b.Property(x => x.Event).HasConversion<int>(); b.Property(x => x.PreviousStatus).HasConversion<int>(); b.Property(x => x.NewStatus).HasConversion<int>(); b.Property(x => x.Message).HasMaxLength(2000); b.HasIndex(x => new { x.TenantId, x.RunId, x.OccurredAtUtc }); b.HasOne(x => x.Run).WithMany(x => x.History).HasForeignKey(nameof(YearEndTaxHistory.TenantId), nameof(YearEndTaxHistory.RunId)).HasPrincipalKey(nameof(YearEndTaxRun.TenantId), nameof(YearEndTaxRun.Id)).OnDelete(DeleteBehavior.Cascade); b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}
