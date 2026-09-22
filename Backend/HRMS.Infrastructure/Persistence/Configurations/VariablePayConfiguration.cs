using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

public sealed class VariablePayPlanConfiguration : IEntityTypeConfiguration<VariablePayPlan>
{
    public void Configure(EntityTypeBuilder<VariablePayPlan> b)
    {
        b.ToTable("VariablePayPlans"); b.HasKey(x => x.Id); b.Property(x => x.Code).HasMaxLength(100).IsRequired(); b.Property(x => x.Name).HasMaxLength(200).IsRequired(); b.Property(x => x.Description).HasMaxLength(1000); b.Property(x => x.CurrencyCode).HasMaxLength(10).IsRequired(); b.Property(x => x.PlanType).HasConversion<int>(); b.Property(x => x.ConcurrencyVersion).HasDefaultValue(1); b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique(); b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class VariablePayPlanVersionConfiguration : IEntityTypeConfiguration<VariablePayPlanVersion>
{
    public void Configure(EntityTypeBuilder<VariablePayPlanVersion> b)
    {
        b.ToTable("VariablePayPlanVersions"); b.HasKey(x => x.Id); b.Property(x => x.Status).HasConversion<int>(); b.Property(x => x.CalculationMethod).HasConversion<int>(); b.Property(x => x.SalaryBasisType).HasConversion<int>(); b.Property(x => x.ProrationMethod).HasConversion<int>(); b.Property(x => x.EligibilityMethod).HasConversion<int>(); b.Property(x => x.PayoutFrequency).HasConversion<int>(); b.Property(x => x.TaxTreatment).HasConversion<int>(); b.Property(x => x.FinalSettlementTreatment).HasConversion<int>(); b.Property(x => x.SelectedSalaryComponentIdsJson).HasMaxLength(4000); b.Property(x => x.ApplicabilityJson).HasMaxLength(4000); b.Property(x => x.Notes).HasMaxLength(1000); foreach (var p in new[] { nameof(VariablePayPlanVersion.Percentage), nameof(VariablePayPlanVersion.FixedAmount), nameof(VariablePayPlanVersion.TargetPercentage), nameof(VariablePayPlanVersion.MinimumAmount), nameof(VariablePayPlanVersion.MaximumAmount), nameof(VariablePayPlanVersion.PerformanceMultiplierMinimum), nameof(VariablePayPlanVersion.PerformanceMultiplierMaximum), nameof(VariablePayPlanVersion.TaxablePercentage) }) b.Property<decimal?>(p).HasPrecision(18, 6); b.HasIndex(x => new { x.TenantId, x.VariablePayPlanId, x.EffectiveFrom }); b.HasOne(x => x.VariablePayPlan).WithMany(x => x.Versions).HasForeignKey(new[] { "TenantId", "VariablePayPlanId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade); b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class VariablePayAwardConfiguration : IEntityTypeConfiguration<VariablePayAward>
{
    public void Configure(EntityTypeBuilder<VariablePayAward> b)
    {
        b.ToTable("VariablePayAwards"); b.HasKey(x => x.Id); b.Property(x => x.AwardNumber).HasMaxLength(40).IsRequired(); b.Property(x => x.CurrencyCode).HasMaxLength(10).IsRequired(); b.Property(x => x.SettlementMethod).HasMaxLength(40).IsRequired(); b.Property(x => x.Status).HasConversion<int>(); b.Property(x => x.ConcurrencyVersion).HasDefaultValue(1); foreach (var p in new[] { nameof(VariablePayAward.SalaryBasisAmount), nameof(VariablePayAward.ProrationFactor), nameof(VariablePayAward.CalculatedAmount), nameof(VariablePayAward.SettledAmount) }) b.Property<decimal>(p).HasPrecision(18, 6); foreach (var p in new[] { nameof(VariablePayAward.TargetAmount), nameof(VariablePayAward.PerformanceMultiplier), nameof(VariablePayAward.ApprovedAmount), nameof(VariablePayAward.TaxableAmount), nameof(VariablePayAward.NonTaxableAmount) }) b.Property<decimal?>(p).HasPrecision(18, 6); b.HasIndex(x => new { x.TenantId, x.AwardNumber }).IsUnique(); b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.VariablePayPlanVersionId, x.AwardPeriodFrom, x.AwardPeriodTo }).IsUnique(); b.HasOne(x => x.VariablePayPlan).WithMany().HasForeignKey(new[] { "TenantId", "VariablePayPlanId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.VariablePayPlanVersion).WithMany().HasForeignKey(new[] { "TenantId", "VariablePayPlanVersionId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.Employee).WithMany().HasForeignKey(new[] { "TenantId", "EmployeeId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class VariablePaySettlementConfiguration : IEntityTypeConfiguration<VariablePaySettlement>
{
    public void Configure(EntityTypeBuilder<VariablePaySettlement> b)
    {
        b.ToTable("VariablePaySettlements"); b.HasKey(x => x.Id); b.Property(x => x.SettlementType).HasConversion<int>(); b.Property(x => x.Reference).HasMaxLength(200); foreach (var p in new[] { nameof(VariablePaySettlement.Amount), nameof(VariablePaySettlement.TaxableAmount), nameof(VariablePaySettlement.NonTaxableAmount) }) b.Property<decimal>(p).HasPrecision(18, 6); b.HasIndex(x => new { x.TenantId, x.VariablePayAwardId, x.SettlementType, x.PayrollRunId, x.FinalSettlementId }).IsUnique(); b.HasOne(x => x.VariablePayAward).WithMany(x => x.Settlements).HasForeignKey(new[] { "TenantId", "VariablePayAwardId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.Employee).WithMany().HasForeignKey(new[] { "TenantId", "EmployeeId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class VariablePayAwardHistoryConfiguration : IEntityTypeConfiguration<VariablePayAwardHistory>
{
    public void Configure(EntityTypeBuilder<VariablePayAwardHistory> b)
    {
        b.ToTable("VariablePayAwardHistories"); b.HasKey(x => x.Id); b.Property(x => x.EventType).HasConversion<int>(); b.Property(x => x.PreviousStatus).HasConversion<int>(); b.Property(x => x.NewStatus).HasConversion<int>(); b.Property(x => x.Reason).HasMaxLength(1000); b.Property(x => x.SourceType).HasMaxLength(100); foreach (var p in new[] { nameof(VariablePayAwardHistory.OriginalAmount), nameof(VariablePayAwardHistory.NewAmount) }) b.Property<decimal?>(p).HasPrecision(18, 6); b.HasIndex(x => new { x.TenantId, x.VariablePayAwardId, x.OccurredAtUtc }); b.HasOne(x => x.VariablePayAward).WithMany(x => x.History).HasForeignKey(new[] { "TenantId", "VariablePayAwardId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade); b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class VariablePayNumberSequenceConfiguration : IEntityTypeConfiguration<VariablePayNumberSequence>
{
    public void Configure(EntityTypeBuilder<VariablePayNumberSequence> b)
    {
        b.ToTable("VariablePayNumberSequences"); b.HasKey(x => x.Id); b.Property(x => x.ConcurrencyVersion).HasDefaultValue(1); b.HasIndex(x => new { x.TenantId, x.Year }).IsUnique(); b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}
