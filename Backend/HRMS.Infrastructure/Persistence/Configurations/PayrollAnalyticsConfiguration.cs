using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

public sealed class PayrollVarianceControlConfiguration : IEntityTypeConfiguration<PayrollVarianceControl>
{
    public void Configure(EntityTypeBuilder<PayrollVarianceControl> b)
    {
        b.ToTable("PayrollVarianceControls"); b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.TenantId, x.Code, x.EffectiveFrom }).IsUnique();
        b.Property(x => x.Code).HasMaxLength(100).IsRequired(); b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.AbsoluteThreshold).HasPrecision(18, 4); b.Property(x => x.PercentageThreshold).HasPrecision(18, 6);
        b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PayrollAnalyticsSnapshotConfiguration : IEntityTypeConfiguration<PayrollAnalyticsSnapshot>
{
    public void Configure(EntityTypeBuilder<PayrollAnalyticsSnapshot> b)
    {
        b.ToTable("PayrollAnalyticsSnapshots"); b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.TenantId, x.PayrollRunId, x.SnapshotType, x.DataVersion }).IsUnique();
        b.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
        b.Property(x => x.GrossTotal).HasPrecision(18, 4); b.Property(x => x.EarningsTotal).HasPrecision(18, 4); b.Property(x => x.DeductionTotal).HasPrecision(18, 4); b.Property(x => x.EmployerContributionTotal).HasPrecision(18, 4); b.Property(x => x.TaxTotal).HasPrecision(18, 4); b.Property(x => x.NetPayTotal).HasPrecision(18, 4); b.Property(x => x.ReimbursementTotal).HasPrecision(18, 4); b.Property(x => x.LoanRecoveryTotal).HasPrecision(18, 4); b.Property(x => x.VariablePayTotal).HasPrecision(18, 4); b.Property(x => x.AdjustmentTotal).HasPrecision(18, 4); b.Property(x => x.FinalSettlementTotal).HasPrecision(18, 4);
        b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.PayrollRun).WithMany().HasForeignKey(x => x.PayrollRunId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PayrollReconciliationConfiguration : IEntityTypeConfiguration<PayrollReconciliation>
{
    public void Configure(EntityTypeBuilder<PayrollReconciliation> b)
    {
        b.ToTable("PayrollReconciliations"); b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.TenantId, x.PayrollRunId, x.ReconciliationType, x.Version }).IsUnique();
        b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.PayrollRun).WithMany().HasForeignKey(x => x.PayrollRunId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Findings).WithOne(x => x.Reconciliation).HasForeignKey(x => x.PayrollReconciliationId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class PayrollReconciliationFindingConfiguration : IEntityTypeConfiguration<PayrollReconciliationFinding>
{
    public void Configure(EntityTypeBuilder<PayrollReconciliationFinding> b)
    {
        b.ToTable("PayrollReconciliationFindings"); b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.TenantId, x.PayrollReconciliationId, x.ControlCode, x.EmployeeId, x.Status });
        b.Property(x => x.ControlCode).HasMaxLength(100).IsRequired(); b.Property(x => x.Message).HasMaxLength(1000).IsRequired();
        b.Property(x => x.ExpectedValue).HasPrecision(18, 4); b.Property(x => x.ActualValue).HasPrecision(18, 4); b.Property(x => x.Difference).HasPrecision(18, 4); b.Property(x => x.VariancePercent).HasPrecision(18, 6);
        b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PayrollAnomalyFlagConfiguration : IEntityTypeConfiguration<PayrollAnomalyFlag>
{
    public void Configure(EntityTypeBuilder<PayrollAnomalyFlag> b)
    {
        b.ToTable("PayrollAnomalyFlags"); b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.TenantId, x.PayrollRunId, x.AnomalyType, x.EmployeeId, x.Status });
        b.Property(x => x.Message).HasMaxLength(1000).IsRequired(); b.Property(x => x.CurrentValue).HasPrecision(18, 4); b.Property(x => x.ComparisonValue).HasPrecision(18, 4); b.Property(x => x.Difference).HasPrecision(18, 4); b.Property(x => x.VariancePercent).HasPrecision(18, 6);
        b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}
