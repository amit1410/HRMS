using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

internal static class PayrollRetroSettlementMapping
{
    public static void Tenant<TEntity>(EntityTypeBuilder<TEntity> b) where TEntity : class, HRMS.Domain.Common.ITenantEntity => b.HasOne<Tenant>().WithMany().HasForeignKey(nameof(HRMS.Domain.Common.ITenantEntity.TenantId)).OnDelete(DeleteBehavior.Restrict);
}

public sealed class PayrollRetroCaseConfiguration : IEntityTypeConfiguration<PayrollRetroCase>
{
    public void Configure(EntityTypeBuilder<PayrollRetroCase> b)
    {
        b.ToTable("PayrollRetroCases"); b.HasKey(x => x.Id); b.Property(x => x.TriggerType).HasConversion<int>(); b.Property(x => x.Status).HasConversion<int>(); b.Property(x => x.Notes).HasMaxLength(2000);
        b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.Status }); b.HasIndex(x => new { x.TenantId, x.EffectiveFrom });
        b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.Employee).WithMany().HasForeignKey(new[] { "TenantId", "EmployeeId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PayrollRetroResultConfiguration : IEntityTypeConfiguration<PayrollRetroResult>
{
    public void Configure(EntityTypeBuilder<PayrollRetroResult> b)
    {
        b.ToTable("PayrollRetroResults"); b.HasKey(x => x.Id); foreach (var p in new[] { nameof(PayrollRetroResult.OriginalGross), nameof(PayrollRetroResult.CorrectedGross), nameof(PayrollRetroResult.GrossDifference), nameof(PayrollRetroResult.OriginalDeductions), nameof(PayrollRetroResult.CorrectedDeductions), nameof(PayrollRetroResult.DeductionDifference), nameof(PayrollRetroResult.OriginalNet), nameof(PayrollRetroResult.CorrectedNet), nameof(PayrollRetroResult.NetDifference) }) b.Property<decimal>(p).HasPrecision(18, 2); b.Property(x => x.CurrencyCode).HasMaxLength(10).IsRequired(); b.HasIndex(x => new { x.TenantId, x.PayrollRetroCaseId });
        b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.RetroCase).WithMany(x => x.Results).HasForeignKey(new[] { "TenantId", "PayrollRetroCaseId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade); b.HasOne(x => x.Employee).WithMany().HasForeignKey(new[] { "TenantId", "EmployeeId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.OriginalPayrollRun).WithMany().HasForeignKey(new[] { "TenantId", "OriginalPayrollRunId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.OriginalPayrollResult).WithMany().HasForeignKey(new[] { "TenantId", "OriginalPayrollResultId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PayrollRetroComponentConfiguration : IEntityTypeConfiguration<PayrollRetroComponent>
{
    public void Configure(EntityTypeBuilder<PayrollRetroComponent> b)
    {
        b.ToTable("PayrollRetroComponents"); b.HasKey(x => x.Id); b.Property(x => x.StatutoryType).HasConversion<int>(); b.Property(x => x.ComponentCode).HasMaxLength(100).IsRequired(); b.Property(x => x.ComponentName).HasMaxLength(200).IsRequired(); foreach (var p in new[] { nameof(PayrollRetroComponent.OriginalAmount), nameof(PayrollRetroComponent.CorrectedAmount), nameof(PayrollRetroComponent.DifferenceAmount) }) b.Property<decimal>(p).HasPrecision(18, 2); b.HasIndex(x => new { x.TenantId, x.PayrollRetroResultId });
        b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.RetroResult).WithMany(x => x.Components).HasForeignKey(new[] { "TenantId", "PayrollRetroResultId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade); b.HasOne(x => x.SalaryComponent).WithMany().HasForeignKey(new[] { "TenantId", "SalaryComponentId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PayrollAdjustmentConfiguration : IEntityTypeConfiguration<PayrollAdjustment>
{
    public void Configure(EntityTypeBuilder<PayrollAdjustment> b)
    {
        b.ToTable("PayrollAdjustments"); b.HasKey(x => x.Id); b.Property(x => x.AdjustmentType).HasConversion<int>(); b.Property(x => x.Status).HasConversion<int>(); b.Property(x => x.Direction).HasConversion<int>(); b.Property(x => x.TaxTreatment).HasConversion<int>(); b.Property(x => x.StatutoryTreatment).HasConversion<int>(); b.Property(x => x.SettlementMethod).HasConversion<int>(); b.Property(x => x.SourceType).HasMaxLength(100).IsRequired(); b.Property(x => x.ComponentCode).HasMaxLength(100).IsRequired(); b.Property(x => x.Description).HasMaxLength(500).IsRequired(); b.Property(x => x.AdjustmentNumber).HasMaxLength(60).IsRequired(); b.Property(x => x.CurrencyCode).HasMaxLength(10).IsRequired(); b.Property(x => x.Reason).HasMaxLength(2000); b.Property(x => x.Amount).HasPrecision(18, 2); b.Property(x => x.AppliedAmount).HasPrecision(18, 2); b.Property(x => x.ConcurrencyVersion).HasDefaultValue(1).IsConcurrencyToken(); b.HasIndex(x => new { x.TenantId, x.AdjustmentNumber }).IsUnique(); b.HasIndex(x => new { x.TenantId, x.SourceType, x.SourceId }).IsUnique(); b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.Status }); b.HasOne(x => x.ReasonCode).WithMany(x => x.Adjustments).HasForeignKey(new[] { "TenantId", "ReasonCodeId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.PayrollPeriod).WithMany().HasForeignKey(new[] { "TenantId", "PayrollPeriodId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.Employee).WithMany().HasForeignKey(new[] { "TenantId", "EmployeeId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.TargetPayrollRun).WithMany().HasForeignKey(new[] { "TenantId", "TargetPayrollRunId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PayrollAdjustmentReasonConfiguration : IEntityTypeConfiguration<PayrollAdjustmentReason>
{
    public void Configure(EntityTypeBuilder<PayrollAdjustmentReason> b) { b.ToTable("PayrollAdjustmentReasons"); b.HasKey(x => x.Id); b.Property(x => x.Code).HasMaxLength(60).IsRequired(); b.Property(x => x.Name).HasMaxLength(150).IsRequired(); b.Property(x => x.Description).HasMaxLength(500); b.Property(x => x.AllowedAdjustmentTypes).HasMaxLength(2000).IsRequired(); b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique(); b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict); }
}

public sealed class PayrollAdjustmentApplicationConfiguration : IEntityTypeConfiguration<PayrollAdjustmentApplication>
{
    public void Configure(EntityTypeBuilder<PayrollAdjustmentApplication> b) { b.ToTable("PayrollAdjustmentApplications"); b.HasKey(x => x.Id); b.Property(x => x.AppliedAmount).HasPrecision(18, 2); b.HasIndex(x => new { x.TenantId, x.PayrollAdjustmentId, x.PayrollRunId }).IsUnique(); b.HasIndex(x => new { x.TenantId, x.PayrollAdjustmentId, x.FinalSettlementId }).IsUnique(); b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.Adjustment).WithMany(x => x.Applications).HasForeignKey(new[] { "TenantId", "PayrollAdjustmentId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.PayrollRun).WithMany().HasForeignKey(new[] { "TenantId", "PayrollRunId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.PayrollResult).WithMany().HasForeignKey(new[] { "TenantId", "PayrollResultId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.FinalSettlement).WithMany().HasForeignKey(new[] { "TenantId", "FinalSettlementId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); }
}

public sealed class PayrollCorrectionSnapshotConfiguration : IEntityTypeConfiguration<PayrollCorrectionSnapshot>
{
    public void Configure(EntityTypeBuilder<PayrollCorrectionSnapshot> b) { b.ToTable("PayrollCorrectionSnapshots"); b.HasKey(x => x.Id); foreach (var p in new[] { nameof(PayrollCorrectionSnapshot.OriginalGross), nameof(PayrollCorrectionSnapshot.OriginalDeduction), nameof(PayrollCorrectionSnapshot.OriginalNet), nameof(PayrollCorrectionSnapshot.CorrectedGross), nameof(PayrollCorrectionSnapshot.CorrectedDeduction), nameof(PayrollCorrectionSnapshot.CorrectedNet), nameof(PayrollCorrectionSnapshot.DeltaGross), nameof(PayrollCorrectionSnapshot.DeltaDeduction), nameof(PayrollCorrectionSnapshot.DeltaNet) }) b.Property<decimal>(p).HasPrecision(18, 2); b.HasIndex(x => new { x.TenantId, x.PayrollAdjustmentId }).IsUnique(); b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.Adjustment).WithMany().HasForeignKey(new[] { "TenantId", "PayrollAdjustmentId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.OriginalPayrollRun).WithMany().HasForeignKey(new[] { "TenantId", "OriginalPayrollRunId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.OriginalPayrollResult).WithMany().HasForeignKey(new[] { "TenantId", "OriginalPayrollResultId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); }
}

public sealed class PayrollReversalConfiguration : IEntityTypeConfiguration<PayrollReversal>
{
    public void Configure(EntityTypeBuilder<PayrollReversal> b) { b.ToTable("PayrollReversals"); b.HasKey(x => x.Id); b.Property(x => x.Status).HasConversion<int>(); b.Property(x => x.Reason).HasMaxLength(2000).IsRequired(); b.HasIndex(x => new { x.TenantId, x.OriginalPayrollRunId }).IsUnique(); b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.OriginalPayrollRun).WithMany().HasForeignKey(new[] { "TenantId", "OriginalPayrollRunId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.ReversalRun).WithMany().HasForeignKey(new[] { "TenantId", "ReversalRunId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.OriginalPayrollResult).WithMany().HasForeignKey(new[] { "TenantId", "OriginalPayrollResultId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); }
}

public sealed class PayrollAdjustmentHistoryConfiguration : IEntityTypeConfiguration<PayrollAdjustmentHistory>
{
    public void Configure(EntityTypeBuilder<PayrollAdjustmentHistory> b) { b.ToTable("PayrollAdjustmentHistories"); b.HasKey(x => x.Id); b.Property(x => x.EventType).HasConversion<int>(); b.Property(x => x.PreviousStatus).HasConversion<int>(); b.Property(x => x.NewStatus).HasConversion<int>(); b.Property(x => x.Amount).HasPrecision(18, 2); b.Property(x => x.Reason).HasMaxLength(2000); b.HasIndex(x => new { x.TenantId, x.PayrollAdjustmentId, x.OccurredAtUtc }); b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.Adjustment).WithMany(x => x.History).HasForeignKey(new[] { "TenantId", "PayrollAdjustmentId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade); }
}

public sealed class PayrollAdjustmentNumberSequenceConfiguration : IEntityTypeConfiguration<PayrollAdjustmentNumberSequence>
{
    public void Configure(EntityTypeBuilder<PayrollAdjustmentNumberSequence> b) { b.ToTable("PayrollAdjustmentNumberSequences"); b.HasKey(x => x.Id); b.HasIndex(x => new { x.TenantId, x.Year }).IsUnique(); b.Property(x => x.NextValue).IsConcurrencyToken(); b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict); }
}

public sealed class PayrollRetroHistoryConfiguration : IEntityTypeConfiguration<PayrollRetroHistory>
{
    public void Configure(EntityTypeBuilder<PayrollRetroHistory> b) { b.ToTable("PayrollRetroHistories"); b.HasKey(x => x.Id); b.Property(x => x.ChangeType).HasConversion<int>(); b.Property(x => x.Message).HasMaxLength(2000); b.HasIndex(x => new { x.TenantId, x.PayrollRetroCaseId, x.ChangedAtUtc }); b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.RetroCase).WithMany(x => x.History).HasForeignKey(new[] { "TenantId", "PayrollRetroCaseId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade); }
}

public sealed class FinalSettlementCaseConfiguration : IEntityTypeConfiguration<FinalSettlementCase>
{
    public void Configure(EntityTypeBuilder<FinalSettlementCase> b)
    {
        b.ToTable("FinalSettlementCases"); b.HasKey(x => x.Id); b.Property(x => x.Status).HasConversion<int>(); b.Property(x => x.SeparationReason).HasConversion<int>(); b.Property(x => x.CurrencyCode).HasMaxLength(10).IsRequired(); b.Property(x => x.GrossPayable).HasPrecision(18, 2); b.Property(x => x.TotalDeductions).HasPrecision(18, 2); b.Property(x => x.NetSettlement).HasPrecision(18, 2); b.Property(x => x.EmployeeCodeSnapshot).HasMaxLength(50).IsRequired(); b.Property(x => x.EmployeeNameSnapshot).HasMaxLength(200).IsRequired(); b.Property(x => x.DepartmentSnapshot).HasMaxLength(200); b.Property(x => x.DesignationSnapshot).HasMaxLength(200); b.Property(x => x.LocationSnapshot).HasMaxLength(200); b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.SeparationDate }).IsUnique(); b.HasIndex(x => new { x.TenantId, x.Status }); b.Property(x => x.ConcurrencyVersion).HasDefaultValue(1);
        b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.Employee).WithMany().HasForeignKey(new[] { "TenantId", "EmployeeId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.PayrollRun).WithMany().HasForeignKey(new[] { "TenantId", "PayrollRunId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.Property(x => x.ConcurrencyVersion).IsConcurrencyToken();
    }
}

public sealed class FinalSettlementLineConfiguration : IEntityTypeConfiguration<FinalSettlementLine>
{
    public void Configure(EntityTypeBuilder<FinalSettlementLine> b) { b.ToTable("FinalSettlementLines"); b.HasKey(x => x.Id); b.Property(x => x.LineType).HasConversion<int>(); b.Property(x => x.ComponentCode).HasMaxLength(100).IsRequired(); b.Property(x => x.Description).HasMaxLength(500).IsRequired(); b.Property(x => x.Amount).HasPrecision(18, 2); b.Property(x => x.SourceType).HasMaxLength(100).IsRequired(); b.HasIndex(x => new { x.TenantId, x.FinalSettlementCaseId, x.Sequence }); b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.Settlement).WithMany(x => x.Lines).HasForeignKey(new[] { "TenantId", "FinalSettlementCaseId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade); }
}

public sealed class FinalSettlementHistoryConfiguration : IEntityTypeConfiguration<FinalSettlementHistory>
{
    public void Configure(EntityTypeBuilder<FinalSettlementHistory> b) { b.ToTable("FinalSettlementHistories"); b.HasKey(x => x.Id); b.Property(x => x.ChangeType).HasConversion<int>(); b.Property(x => x.Message).HasMaxLength(2000); b.HasIndex(x => new { x.TenantId, x.FinalSettlementCaseId, x.ChangedAtUtc }); b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.Settlement).WithMany(x => x.History).HasForeignKey(new[] { "TenantId", "FinalSettlementCaseId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade); }
}
