using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

public sealed class TaxDeclarationCycleConfiguration : IEntityTypeConfiguration<TaxDeclarationCycle>
{
    public void Configure(EntityTypeBuilder<TaxDeclarationCycle> b)
    {
        b.ToTable("TaxDeclarationCycles"); b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(80).IsRequired(); b.Property(x => x.Name).HasMaxLength(200).IsRequired(); b.Property(x => x.Status).HasConversion<int>(); b.Property(x => x.ConcurrencyVersion).HasDefaultValue(1).IsConcurrencyToken();
        b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique(); b.HasIndex(x => new { x.TenantId, x.FinancialYear, x.Status });
        b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class TaxDeclarationCategoryConfiguration : IEntityTypeConfiguration<TaxDeclarationCategory>
{
    public void Configure(EntityTypeBuilder<TaxDeclarationCategory> b)
    {
        b.ToTable("TaxDeclarationCategories"); b.HasKey(x => x.Id); b.Property(x => x.Code).HasMaxLength(80).IsRequired(); b.Property(x => x.Name).HasMaxLength(200).IsRequired(); b.Property(x => x.Description).HasMaxLength(1000); b.Property(x => x.CategoryType).HasConversion<int>();
        b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique(); b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class TaxDeclarationItemConfiguration : IEntityTypeConfiguration<TaxDeclarationItem>
{
    public void Configure(EntityTypeBuilder<TaxDeclarationItem> b)
    {
        b.ToTable("TaxDeclarationItems"); b.HasKey(x => x.Id); b.Property(x => x.Code).HasMaxLength(80).IsRequired(); b.Property(x => x.Name).HasMaxLength(200).IsRequired(); b.Property(x => x.Description).HasMaxLength(1000); b.Property(x => x.PayrollTaxInputCode).HasMaxLength(100); b.Property(x => x.StatutoryMappingCode).HasMaxLength(100);
        b.HasIndex(x => new { x.TenantId, x.TaxDeclarationCategoryId, x.Code }).IsUnique(); b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.Category).WithMany(x => x.Items).HasForeignKey(new[] { "TenantId", "TaxDeclarationCategoryId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class EmployeeTaxDeclarationConfiguration : IEntityTypeConfiguration<EmployeeTaxDeclaration>
{
    public void Configure(EntityTypeBuilder<EmployeeTaxDeclaration> b)
    {
        b.ToTable("EmployeeTaxDeclarations"); b.HasKey(x => x.Id); b.Property(x => x.Status).HasConversion<int>(); b.Property(x => x.ConcurrencyVersion).HasDefaultValue(1).IsConcurrencyToken();
        b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.TaxDeclarationCycleId }).IsUnique(); b.HasIndex(x => new { x.TenantId, x.Status });
        b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.Employee).WithMany().HasForeignKey(new[] { "TenantId", "EmployeeId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.Cycle).WithMany(x => x.Declarations).HasForeignKey(new[] { "TenantId", "TaxDeclarationCycleId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class EmployeeTaxDeclarationLineConfiguration : IEntityTypeConfiguration<EmployeeTaxDeclarationLine>
{
    public void Configure(EntityTypeBuilder<EmployeeTaxDeclarationLine> b)
    {
        b.ToTable("EmployeeTaxDeclarationLines"); b.HasKey(x => x.Id); b.Property(x => x.DeclaredAmount).HasPrecision(18, 2); b.Property(x => x.ApprovedAmount).HasPrecision(18, 2); b.Property(x => x.ReferenceNumber).HasMaxLength(150); b.Property(x => x.Notes).HasMaxLength(2000); b.Property(x => x.ReviewerComment).HasMaxLength(2000); b.Property(x => x.Status).HasConversion<int>();
        b.HasIndex(x => new { x.TenantId, x.EmployeeTaxDeclarationId }); b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.Declaration).WithMany(x => x.Lines).HasForeignKey(new[] { "TenantId", "EmployeeTaxDeclarationId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade); b.HasOne(x => x.Category).WithMany().HasForeignKey(new[] { "TenantId", "TaxDeclarationCategoryId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.Item).WithMany(x => x.Lines).HasForeignKey(new[] { "TenantId", "TaxDeclarationItemId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class TaxDeclarationProofConfiguration : IEntityTypeConfiguration<TaxDeclarationProof>
{
    public void Configure(EntityTypeBuilder<TaxDeclarationProof> b)
    {
        b.ToTable("TaxDeclarationProofs"); b.HasKey(x => x.Id); b.Property(x => x.FileName).HasMaxLength(255).IsRequired(); b.Property(x => x.ContentType).HasMaxLength(150).IsRequired(); b.Property(x => x.StorageReference).HasMaxLength(1000).IsRequired(); b.Property(x => x.DocumentType).HasMaxLength(100); b.Property(x => x.Status).HasConversion<int>(); b.Property(x => x.Hash).HasMaxLength(200);
        b.HasIndex(x => new { x.TenantId, x.EmployeeTaxDeclarationLineId }); b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.Line).WithMany(x => x.Proofs).HasForeignKey(new[] { "TenantId", "EmployeeTaxDeclarationLineId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class TaxDeclarationAuditEventConfiguration : IEntityTypeConfiguration<TaxDeclarationAuditEvent>
{
    public void Configure(EntityTypeBuilder<TaxDeclarationAuditEvent> b)
    {
        b.ToTable("TaxDeclarationAuditEvents"); b.HasKey(x => x.Id); b.Property(x => x.Action).HasConversion<int>(); b.Property(x => x.OldValue).HasMaxLength(4000); b.Property(x => x.NewValue).HasMaxLength(4000); b.Property(x => x.Comment).HasMaxLength(2000);
        b.HasIndex(x => new { x.TenantId, x.EmployeeTaxDeclarationId, x.OccurredAtUtc }); b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.Declaration).WithMany(x => x.AuditEvents).HasForeignKey(new[] { "TenantId", "EmployeeTaxDeclarationId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}
