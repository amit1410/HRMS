using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

internal static class SeparationBenefitsMapping
{
    public static void Tenant<TEntity>(EntityTypeBuilder<TEntity> b) where TEntity : class, HRMS.Domain.Common.ITenantEntity => b.HasOne<Tenant>().WithMany().HasForeignKey(nameof(HRMS.Domain.Common.ITenantEntity.TenantId)).OnDelete(DeleteBehavior.Restrict);
}

public sealed class GratuityPolicyConfiguration : IEntityTypeConfiguration<GratuityPolicy>
{
    public void Configure(EntityTypeBuilder<GratuityPolicy> b)
    {
        b.ToTable("GratuityPolicies"); b.HasKey(x => x.Id); b.Property(x => x.Code).HasMaxLength(100).IsRequired(); b.Property(x => x.Name).HasMaxLength(200).IsRequired(); b.Property(x => x.Description).HasMaxLength(1000); b.Property(x => x.CurrencyCode).HasMaxLength(10).IsRequired(); b.Property(x => x.ConcurrencyVersion).IsConcurrencyToken(); b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique(); b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class GratuityPolicyVersionConfiguration : IEntityTypeConfiguration<GratuityPolicyVersion>
{
    public void Configure(EntityTypeBuilder<GratuityPolicyVersion> b)
    {
        b.ToTable("GratuityPolicyVersions"); b.HasKey(x => x.Id); b.Property(x => x.Status).HasConversion<int>(); b.Property(x => x.ServiceRoundingMethod).HasConversion<int>(); b.Property(x => x.FormulaType).HasConversion<int>(); b.Property(x => x.WageBasisType).HasConversion<int>(); b.Property(x => x.TaxTreatment).HasConversion<int>(); b.Property(x => x.LeaveEncashmentTaxTreatment).HasConversion<int>(); b.Property(x => x.NoticeSettlementType).HasConversion<int>(); b.Property(x => x.NoticeWageBasisType).HasConversion<int>(); b.Property(x => x.MonetaryRoundingMethod).HasConversion<int>(); foreach (var p in new[] { nameof(GratuityPolicyVersion.NumeratorDays), nameof(GratuityPolicyVersion.DenominatorDays), nameof(GratuityPolicyVersion.ServiceRoundingThresholdMonths), nameof(GratuityPolicyVersion.FixedAmount), nameof(GratuityPolicyVersion.MaximumBenefit), nameof(GratuityPolicyVersion.MinimumBenefit), nameof(GratuityPolicyVersion.TaxablePercentage), nameof(GratuityPolicyVersion.MaximumLeaveEncashmentDays), nameof(GratuityPolicyVersion.LeaveEncashmentDivisor), nameof(GratuityPolicyVersion.NoticeDivisor) }) b.Property<decimal?>(p).HasPrecision(18, 6); b.HasIndex(x => new { x.TenantId, x.GratuityPolicyId, x.EffectiveFrom }); b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.GratuityPolicy).WithMany(x => x.Versions).HasForeignKey(new[] { "TenantId", "GratuityPolicyId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class GratuityCalculationConfiguration : IEntityTypeConfiguration<GratuityCalculation>
{
    public void Configure(EntityTypeBuilder<GratuityCalculation> b)
    {
        b.ToTable("GratuityCalculations"); b.HasKey(x => x.Id); b.Property(x => x.SeparationReason).HasConversion<int>(); b.Property(x => x.WageBasisType).HasConversion<int>(); b.Property(x => x.Status).HasConversion<int>(); b.Property(x => x.AppliedRoundingRule).HasMaxLength(100).IsRequired(); b.Property(x => x.WageSnapshotJson).HasMaxLength(4000).IsRequired(); b.Property(x => x.CurrencyCode).HasMaxLength(10).IsRequired(); b.Property(x => x.ConcurrencyVersion).IsConcurrencyToken(); b.Property(x => x.TotalServiceMonths).HasPrecision(18, 6); b.Property(x => x.EligibleServiceUnits).HasPrecision(18, 6); b.Property(x => x.WageBasisAmount).HasPrecision(18, 6); b.Property(x => x.NumeratorDays).HasPrecision(18, 6); b.Property(x => x.DenominatorDays).HasPrecision(18, 6); b.Property(x => x.GrossCalculatedAmount).HasPrecision(18, 6); b.Property(x => x.CapAmount).HasPrecision(18, 6); b.Property(x => x.FinalGratuityAmount).HasPrecision(18, 6); b.Property(x => x.TaxableAmount).HasPrecision(18, 6); b.Property(x => x.NonTaxableAmount).HasPrecision(18, 6); b.HasIndex(x => new { x.TenantId, x.FinalSettlementId }).IsUnique().HasFilter("[FinalSettlementId] IS NOT NULL"); b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.ServiceEndDate }); b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.Employee).WithMany().HasForeignKey(new[] { "TenantId", "EmployeeId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.FinalSettlement).WithMany().HasForeignKey(new[] { "TenantId", "FinalSettlementId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.GratuityPolicy).WithMany().HasForeignKey(new[] { "TenantId", "GratuityPolicyId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.GratuityPolicyVersion).WithMany().HasForeignKey(new[] { "TenantId", "GratuityPolicyVersionId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class GratuityOverrideConfiguration : IEntityTypeConfiguration<GratuityOverride>
{
    public void Configure(EntityTypeBuilder<GratuityOverride> b)
    {
        b.ToTable("GratuityOverrides"); b.HasKey(x => x.Id); b.Property(x => x.Reason).HasMaxLength(1000).IsRequired(); b.Property(x => x.OriginalAmount).HasPrecision(18, 2); b.Property(x => x.OverrideAmount).HasPrecision(18, 2); b.HasIndex(x => new { x.TenantId, x.GratuityCalculationId, x.ApprovedAtUtc }); b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.GratuityCalculation).WithMany().HasForeignKey(new[] { "TenantId", "GratuityCalculationId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class LeaveEncashmentCalculationConfiguration : IEntityTypeConfiguration<LeaveEncashmentCalculation>
{
    public void Configure(EntityTypeBuilder<LeaveEncashmentCalculation> b)
    {
        b.ToTable("LeaveEncashmentCalculations"); b.HasKey(x => x.Id); foreach (var p in new[] { nameof(LeaveEncashmentCalculation.EligibleDays), nameof(LeaveEncashmentCalculation.EncashableDays), nameof(LeaveEncashmentCalculation.WageBasisAmount), nameof(LeaveEncashmentCalculation.Divisor), nameof(LeaveEncashmentCalculation.GrossAmount), nameof(LeaveEncashmentCalculation.TaxableAmount), nameof(LeaveEncashmentCalculation.NonTaxableAmount) }) b.Property<decimal>(p).HasPrecision(18, 6); b.Property(x => x.SourceBalanceReference).HasMaxLength(100).IsRequired(); b.HasIndex(x => new { x.TenantId, x.FinalSettlementId, x.LeaveTypeId }).IsUnique(); b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.Employee).WithMany().HasForeignKey(new[] { "TenantId", "EmployeeId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.FinalSettlement).WithMany().HasForeignKey(new[] { "TenantId", "FinalSettlementId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade); b.HasOne(x => x.LeaveType).WithMany().HasForeignKey(new[] { "TenantId", "LeaveTypeId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class NoticeSettlementCalculationConfiguration : IEntityTypeConfiguration<NoticeSettlementCalculation>
{
    public void Configure(EntityTypeBuilder<NoticeSettlementCalculation> b)
    {
        b.ToTable("NoticeSettlementCalculations"); b.HasKey(x => x.Id); b.Property(x => x.Type).HasConversion<int>(); foreach (var p in new[] { nameof(NoticeSettlementCalculation.RequiredDays), nameof(NoticeSettlementCalculation.ServedDays), nameof(NoticeSettlementCalculation.DifferenceDays), nameof(NoticeSettlementCalculation.WageBasisAmount), nameof(NoticeSettlementCalculation.Divisor), nameof(NoticeSettlementCalculation.Amount), nameof(NoticeSettlementCalculation.TaxableAmount), nameof(NoticeSettlementCalculation.NonTaxableAmount) }) b.Property<decimal>(p).HasPrecision(18, 6); b.HasIndex(x => new { x.TenantId, x.FinalSettlementId }).IsUnique(); b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.Employee).WithMany().HasForeignKey(new[] { "TenantId", "EmployeeId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.FinalSettlement).WithMany().HasForeignKey(new[] { "TenantId", "FinalSettlementId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class SeparationBenefitHistoryConfiguration : IEntityTypeConfiguration<SeparationBenefitHistory>
{
    public void Configure(EntityTypeBuilder<SeparationBenefitHistory> b)
    {
        b.ToTable("SeparationBenefitHistories"); b.HasKey(x => x.Id); b.Property(x => x.EventType).HasConversion<int>(); b.Property(x => x.SourceType).HasMaxLength(100); b.Property(x => x.Reason).HasMaxLength(1000); b.Property(x => x.SnapshotJson).HasMaxLength(4000).IsRequired(); foreach (var p in new[] { nameof(SeparationBenefitHistory.OriginalAmount), nameof(SeparationBenefitHistory.FinalAmount) }) b.Property<decimal?>(p).HasPrecision(18, 2); b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.OccurredAtUtc }); b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.GratuityPolicy).WithMany(x => x.History).HasForeignKey(new[] { "TenantId", "GratuityPolicyId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.GratuityPolicyVersion).WithMany().HasForeignKey(new[] { "TenantId", "GratuityPolicyVersionId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.Employee).WithMany().HasForeignKey(new[] { "TenantId", "EmployeeId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.FinalSettlement).WithMany().HasForeignKey(new[] { "TenantId", "FinalSettlementId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
