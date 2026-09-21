using HRMS.Domain.Common;
using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

internal static class ReimbursementMapping
{
    public static void Tenant<TEntity>(EntityTypeBuilder<TEntity> b) where TEntity : class, ITenantEntity =>
        b.HasOne<Tenant>("Tenant").WithMany().HasForeignKey(nameof(ITenantEntity.TenantId)).OnDelete(DeleteBehavior.Restrict);
}

public sealed class ReimbursementCategoryConfiguration : IEntityTypeConfiguration<ReimbursementCategory>
{
    public void Configure(EntityTypeBuilder<ReimbursementCategory> b)
    {
        b.ToTable("ReimbursementCategories"); b.HasKey(x => x.Id); b.Property(x => x.Code).HasMaxLength(50).IsRequired(); b.Property(x => x.Name).HasMaxLength(200).IsRequired(); b.Property(x => x.Description).HasMaxLength(1000); b.Property(x => x.CurrencyCode).HasMaxLength(10).IsRequired(); b.Property(x => x.CategoryType).HasConversion<int>(); b.Property(x => x.TaxTreatment).HasConversion<int>(); b.Property(x => x.DefaultSettlementMethod).HasConversion<int>(); b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique(); ReimbursementMapping.Tenant(b);
    }
}

public sealed class ReimbursementPolicyVersionConfiguration : IEntityTypeConfiguration<ReimbursementPolicyVersion>
{
    public void Configure(EntityTypeBuilder<ReimbursementPolicyVersion> b)
    {
        b.ToTable("ReimbursementPolicyVersions"); b.HasKey(x => x.Id); b.Property(x => x.MinClaimAmount).HasPrecision(18, 2); b.Property(x => x.MaxClaimAmount).HasPrecision(18, 2); b.Property(x => x.PerTransactionLimit).HasPrecision(18, 2); b.Property(x => x.MonthlyLimit).HasPrecision(18, 2); b.Property(x => x.YearlyLimit).HasPrecision(18, 2); b.Property(x => x.ReceiptRequiredAbove).HasPrecision(18, 2); b.Property(x => x.TaxTreatment).HasConversion<int>(); b.Property(x => x.SettlementMethod).HasConversion<int>(); b.Property(x => x.Status).HasConversion<int>(); b.Property(x => x.EligibilityJson).HasMaxLength(4000); b.HasIndex(x => new { x.TenantId, x.ReimbursementCategoryId, x.EffectiveFrom }); b.HasOne(x => x.Category).WithMany(x => x.Versions).HasForeignKey(new[] { "TenantId", "ReimbursementCategoryId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); ReimbursementMapping.Tenant(b);
    }
}

public sealed class ReimbursementClaimConfiguration : IEntityTypeConfiguration<ReimbursementClaim>
{
    public void Configure(EntityTypeBuilder<ReimbursementClaim> b)
    {
        b.ToTable("ReimbursementClaims"); b.HasKey(x => x.Id); b.Property(x => x.ClaimNumber).HasMaxLength(60).IsRequired(); b.Property(x => x.CurrencyCode).HasMaxLength(10).IsRequired(); b.Property(x => x.Status).HasConversion<int>(); b.Property(x => x.SettlementMethod).HasConversion<int>(); foreach (var p in new[] { nameof(ReimbursementClaim.TotalClaimedAmount), nameof(ReimbursementClaim.TotalEligibleAmount), nameof(ReimbursementClaim.TotalApprovedAmount), nameof(ReimbursementClaim.TaxableAmount), nameof(ReimbursementClaim.NonTaxableAmount), nameof(ReimbursementClaim.SettledAmount) }) b.Property<decimal>(p).HasPrecision(18, 2); b.HasIndex(x => new { x.TenantId, x.ClaimNumber }).IsUnique(); b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.Status }); b.Property(x => x.ConcurrencyVersion).IsConcurrencyToken().HasDefaultValue(1); b.HasOne(x => x.Employee).WithMany().HasForeignKey(new[] { "TenantId", "EmployeeId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); ReimbursementMapping.Tenant(b);
    }
}

public sealed class ReimbursementClaimLineConfiguration : IEntityTypeConfiguration<ReimbursementClaimLine>
{
    public void Configure(EntityTypeBuilder<ReimbursementClaimLine> b)
    {
        b.ToTable("ReimbursementClaimLines"); b.HasKey(x => x.Id); b.Property(x => x.Description).HasMaxLength(1000).IsRequired(); b.Property(x => x.MerchantName).HasMaxLength(200); b.Property(x => x.ReferenceNumber).HasMaxLength(200); b.Property(x => x.ApprovalComment).HasMaxLength(1000); b.Property(x => x.Status).HasConversion<int>(); b.Property(x => x.ReceiptStatus).HasConversion<int>(); foreach (var p in new[] { nameof(ReimbursementClaimLine.ClaimedAmount), nameof(ReimbursementClaimLine.EligibleAmount), nameof(ReimbursementClaimLine.ApprovedAmount), nameof(ReimbursementClaimLine.TaxableAmount), nameof(ReimbursementClaimLine.NonTaxableAmount) }) b.Property<decimal>(p).HasPrecision(18, 2); b.HasIndex(x => new { x.TenantId, x.ReimbursementClaimId }); b.HasOne(x => x.Claim).WithMany(x => x.Lines).HasForeignKey(new[] { "TenantId", "ReimbursementClaimId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade); b.HasOne(x => x.Category).WithMany().HasForeignKey(new[] { "TenantId", "ReimbursementCategoryId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.PolicyVersion).WithMany(x => x.ClaimLines).HasForeignKey(new[] { "TenantId", "PolicyVersionId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); ReimbursementMapping.Tenant(b);
    }
}

public sealed class ReimbursementAttachmentConfiguration : IEntityTypeConfiguration<ReimbursementAttachment>
{
    public void Configure(EntityTypeBuilder<ReimbursementAttachment> b)
    {
        b.ToTable("ReimbursementAttachments"); b.HasKey(x => x.Id); b.Property(x => x.FileName).HasMaxLength(260).IsRequired(); b.Property(x => x.ContentType).HasMaxLength(200).IsRequired(); b.Property(x => x.StorageReference).HasMaxLength(1000).IsRequired(); b.HasIndex(x => new { x.TenantId, x.ReimbursementClaimId }); b.HasOne(x => x.Claim).WithMany(x => x.Attachments).HasForeignKey(new[] { "TenantId", "ReimbursementClaimId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade); b.HasOne(x => x.ClaimLine).WithMany(x => x.Attachments).HasForeignKey(new[] { "TenantId", "ReimbursementClaimLineId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); ReimbursementMapping.Tenant(b);
    }
}

public sealed class ReimbursementSettlementConfiguration : IEntityTypeConfiguration<ReimbursementSettlement>
{
    public void Configure(EntityTypeBuilder<ReimbursementSettlement> b)
    {
        b.ToTable("ReimbursementSettlements"); b.HasKey(x => x.Id); b.Property(x => x.SettlementType).HasConversion<int>(); b.Property(x => x.Reference).HasMaxLength(250); foreach (var p in new[] { nameof(ReimbursementSettlement.Amount), nameof(ReimbursementSettlement.TaxableAmount), nameof(ReimbursementSettlement.NonTaxableAmount) }) b.Property<decimal>(p).HasPrecision(18, 2); b.HasIndex(x => new { x.TenantId, x.ReimbursementClaimId, x.SettlementType }); b.HasIndex(x => new { x.TenantId, x.PayrollResultId }); b.HasIndex(x => new { x.TenantId, x.FinalSettlementId }); b.HasOne(x => x.Claim).WithMany(x => x.Settlements).HasForeignKey(new[] { "TenantId", "ReimbursementClaimId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade); b.HasOne(x => x.ClaimLine).WithMany().HasForeignKey(new[] { "TenantId", "ReimbursementClaimLineId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); ReimbursementMapping.Tenant(b);
    }
}

public sealed class ReimbursementHistoryConfiguration : IEntityTypeConfiguration<ReimbursementHistory>
{
    public void Configure(EntityTypeBuilder<ReimbursementHistory> b)
    {
        b.ToTable("ReimbursementHistories"); b.HasKey(x => x.Id); b.Property(x => x.EventType).HasConversion<int>(); b.Property(x => x.PreviousStatus).HasConversion<int>(); b.Property(x => x.NewStatus).HasConversion<int>(); b.Property(x => x.Reason).HasMaxLength(1000); b.Property(x => x.SourceType).HasMaxLength(100); b.Property(x => x.ClaimedAmount).HasPrecision(18, 2); b.Property(x => x.ApprovedAmount).HasPrecision(18, 2); b.HasIndex(x => new { x.TenantId, x.ReimbursementClaimId, x.OccurredAtUtc }); b.HasOne(x => x.Claim).WithMany(x => x.History).HasForeignKey(new[] { "TenantId", "ReimbursementClaimId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade); ReimbursementMapping.Tenant(b);
    }
}
