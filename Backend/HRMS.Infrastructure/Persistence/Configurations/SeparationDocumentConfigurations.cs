using HRMS.Domain.Entities.Separation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

public sealed class SeparationDocumentTemplateConfiguration : IEntityTypeConfiguration<SeparationDocumentTemplate>
{
    public void Configure(EntityTypeBuilder<SeparationDocumentTemplate> b)
    {
        b.ToTable("SeparationDocumentTemplates"); b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(80).IsRequired(); b.Property(x => x.Name).HasMaxLength(200).IsRequired(); b.Property(x => x.Description).HasMaxLength(1000); b.Property(x => x.DocumentType).HasConversion<int>(); b.Property(x => x.ConcurrencyVersion).IsConcurrencyToken();
        b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique(); b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SeparationDocumentTemplateVersionConfiguration : IEntityTypeConfiguration<SeparationDocumentTemplateVersion>
{
    public void Configure(EntityTypeBuilder<SeparationDocumentTemplateVersion> b)
    {
        b.ToTable("SeparationDocumentTemplateVersions"); b.HasKey(x => x.Id); b.Property(x => x.BodyTemplate).HasMaxLength(100000).IsRequired(); b.Property(x => x.Subject).HasMaxLength(500); b.Property(x => x.HeaderTemplate).HasMaxLength(20000); b.Property(x => x.FooterTemplate).HasMaxLength(20000); b.Property(x => x.PageSettingsJson).HasMaxLength(4000); b.Property(x => x.Status).HasConversion<int>(); b.Property(x => x.ConcurrencyVersion).IsConcurrencyToken();
        b.HasIndex(x => new { x.TenantId, x.TemplateId, x.VersionNumber }).IsUnique(); b.HasIndex(x => new { x.TenantId, x.TemplateId, x.EffectiveFrom }); b.HasOne(x => x.Template).WithMany(x => x.Versions).HasForeignKey(x => new { x.TenantId, x.TemplateId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SeparationGeneratedDocumentConfiguration : IEntityTypeConfiguration<SeparationGeneratedDocument>
{
    public void Configure(EntityTypeBuilder<SeparationGeneratedDocument> b)
    {
        b.ToTable("SeparationGeneratedDocuments"); b.HasKey(x => x.Id); b.Property(x => x.DocumentNumber).HasMaxLength(120).IsRequired(); b.Property(x => x.CustomDocumentCode).HasMaxLength(80); b.Property(x => x.ContentHash).HasMaxLength(128).IsRequired(); b.Property(x => x.FileName).HasMaxLength(255).IsRequired(); b.Property(x => x.MimeType).HasMaxLength(100).IsRequired(); b.Property(x => x.StorageReference).HasMaxLength(1000); b.Property(x => x.SnapshotJson).HasMaxLength(100000).IsRequired(); b.Property(x => x.ContentBase64).HasMaxLength(2000000).IsRequired(); b.Property(x => x.LastReason).HasMaxLength(1000); b.Property(x => x.DocumentType).HasConversion<int>(); b.Property(x => x.Status).HasConversion<int>(); b.Property(x => x.ConcurrencyVersion).IsConcurrencyToken();
        b.HasIndex(x => new { x.TenantId, x.DocumentNumber }).IsUnique(); b.HasIndex(x => new { x.TenantId, x.EmployeeSeparationId, x.DocumentType, x.CurrentDocumentKey }).IsUnique(); b.HasIndex(x => new { x.TenantId, x.EmployeeSeparationId, x.DocumentType, x.Status }); b.HasIndex(x => new { x.TenantId, x.EmployeeId });
        b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.Separation).WithMany().HasForeignKey(x => new { x.TenantId, x.EmployeeSeparationId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.Employee).WithMany().HasForeignKey(x => new { x.TenantId, x.EmployeeId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.TemplateVersion).WithMany(x => x.GeneratedDocuments).HasForeignKey(x => new { x.TenantId, x.TemplateVersionId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.SupersedesDocument).WithMany().HasForeignKey(x => new { x.TenantId, x.SupersedesDocumentId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict).IsRequired(false);
    }
}

public sealed class SeparationDocumentEventConfiguration : IEntityTypeConfiguration<SeparationDocumentEvent>
{
    public void Configure(EntityTypeBuilder<SeparationDocumentEvent> b)
    {
        b.ToTable("SeparationDocumentEvents"); b.HasKey(x => x.Id); b.Property(x => x.EventType).HasConversion<int>(); b.Property(x => x.Reason).HasMaxLength(1000); b.Property(x => x.MetadataJson).HasMaxLength(10000); b.HasIndex(x => new { x.TenantId, x.GeneratedDocumentId, x.OccurredAtUtc }); b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.GeneratedDocument).WithMany(x => x.Events).HasForeignKey(x => new { x.TenantId, x.GeneratedDocumentId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict).IsRequired(false);
    }
}

public sealed class SeparationDocumentNumberSequenceConfiguration : IEntityTypeConfiguration<SeparationDocumentNumberSequence>
{
    public void Configure(EntityTypeBuilder<SeparationDocumentNumberSequence> b)
    {
        b.ToTable("SeparationDocumentNumberSequences"); b.HasKey(x => x.Id); b.Property(x => x.DocumentType).HasConversion<int>(); b.Property(x => x.ConcurrencyVersion).IsConcurrencyToken(); b.HasIndex(x => new { x.TenantId, x.DocumentType, x.Year }).IsUnique(); b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}
