using HRMS.Domain.Common;
using HRMS.Domain.Enums;

namespace HRMS.Domain.Entities.Separation;

public sealed class SeparationDocumentTemplate : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public SeparationDocumentType DocumentType { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool RequiresApproval { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public int ConcurrencyVersion { get; set; } = 1;
    public Tenant? Tenant { get; set; }
    public ICollection<SeparationDocumentTemplateVersion> Versions { get; set; } = new List<SeparationDocumentTemplateVersion>();
}

public sealed class SeparationDocumentTemplateVersion : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid TemplateId { get; set; }
    public int VersionNumber { get; set; }
    public SeparationDocumentTemplateVersionStatus Status { get; set; } = SeparationDocumentTemplateVersionStatus.Draft;
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public string? Subject { get; set; }
    public string BodyTemplate { get; set; } = string.Empty;
    public string? HeaderTemplate { get; set; }
    public string? FooterTemplate { get; set; }
    public string? PageSettingsJson { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public Guid? PublishedByUserId { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
    public int ConcurrencyVersion { get; set; } = 1;
    public Tenant? Tenant { get; set; }
    public SeparationDocumentTemplate? Template { get; set; }
    public ICollection<SeparationGeneratedDocument> GeneratedDocuments { get; set; } = new List<SeparationGeneratedDocument>();
}

public sealed class SeparationGeneratedDocument : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid EmployeeSeparationId { get; set; }
    public Guid EmployeeId { get; set; }
    public SeparationDocumentType DocumentType { get; set; }
    public string? CustomDocumentCode { get; set; }
    public Guid TemplateId { get; set; }
    public Guid TemplateVersionId { get; set; }
    public string DocumentNumber { get; set; } = string.Empty;
    public string CurrentDocumentKey { get; set; } = string.Empty;
    public SeparationGeneratedDocumentStatus Status { get; set; }
    public DateOnly IssueDate { get; set; }
    public DateTime GeneratedAtUtc { get; set; }
    public Guid? GeneratedByUserId { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public Guid? SupersedesDocumentId { get; set; }
    public Guid? SupersededByDocumentId { get; set; }
    public string? StorageReference { get; set; }
    public string ContentHash { get; set; } = string.Empty;
    public string MimeType { get; set; } = "application/pdf";
    public string FileName { get; set; } = string.Empty;
    public string SnapshotJson { get; set; } = string.Empty;
    public string ContentBase64 { get; set; } = string.Empty;
    public string? LastReason { get; set; }
    public int ConcurrencyVersion { get; set; } = 1;
    public Tenant? Tenant { get; set; }
    public EmployeeSeparation? Separation { get; set; }
    public Employee? Employee { get; set; }
    public SeparationDocumentTemplateVersion? TemplateVersion { get; set; }
    public SeparationGeneratedDocument? SupersedesDocument { get; set; }
    public SeparationGeneratedDocument? SupersededByDocument { get; set; }
    public ICollection<SeparationDocumentEvent> Events { get; set; } = new List<SeparationDocumentEvent>();
}

public sealed class SeparationDocumentEvent : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid? TemplateId { get; set; }
    public Guid? TemplateVersionId { get; set; }
    public Guid? GeneratedDocumentId { get; set; }
    public SeparationDocumentEventType EventType { get; set; }
    public Guid? ActorUserId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public string? Reason { get; set; }
    public string? MetadataJson { get; set; }
    public Tenant? Tenant { get; set; }
    public SeparationGeneratedDocument? GeneratedDocument { get; set; }
}

public sealed class SeparationDocumentNumberSequence : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public SeparationDocumentType DocumentType { get; set; }
    public int Year { get; set; }
    public long NextNumber { get; set; } = 1;
    public int ConcurrencyVersion { get; set; } = 1;
    public Tenant? Tenant { get; set; }
}
