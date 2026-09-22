using HRMS.Domain.Common;
using HRMS.Domain.Enums;

namespace HRMS.Domain.Entities;

public sealed class TaxDeclarationCycle : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int FinancialYear { get; set; }
    public DateOnly DeclarationOpenDate { get; set; }
    public DateOnly DeclarationCloseDate { get; set; }
    public DateOnly ProofSubmissionOpenDate { get; set; }
    public DateOnly ProofSubmissionCloseDate { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public TaxDeclarationCycleStatus Status { get; set; } = TaxDeclarationCycleStatus.Draft;
    public Guid? CreatedByUserId { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public int ConcurrencyVersion { get; set; } = 1;
    public Tenant? Tenant { get; set; }
    public ICollection<EmployeeTaxDeclaration> Declarations { get; set; } = new List<EmployeeTaxDeclaration>();
}

public sealed class TaxDeclarationCategory : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public TaxDeclarationCategoryType CategoryType { get; set; }
    public bool RequiresProof { get; set; }
    public bool AllowsMultipleEntries { get; set; } = true;
    public bool Active { get; set; } = true;
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public int DisplayOrder { get; set; }
    public Tenant? Tenant { get; set; }
    public ICollection<TaxDeclarationItem> Items { get; set; } = new List<TaxDeclarationItem>();
}

public sealed class TaxDeclarationItem : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid TaxDeclarationCategoryId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool RequiresProof { get; set; }
    public bool AllowsAmount { get; set; } = true;
    public bool AllowsReferenceNumber { get; set; } = true;
    public bool AllowsDate { get; set; } = true;
    public bool Active { get; set; } = true;
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public string? PayrollTaxInputCode { get; set; }
    public string? StatutoryMappingCode { get; set; }
    public Tenant? Tenant { get; set; }
    public TaxDeclarationCategory? Category { get; set; }
    public ICollection<EmployeeTaxDeclarationLine> Lines { get; set; } = new List<EmployeeTaxDeclarationLine>();
}

public sealed class EmployeeTaxDeclaration : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid TaxDeclarationCycleId { get; set; }
    public EmployeeTaxDeclarationStatus Status { get; set; } = EmployeeTaxDeclarationStatus.Draft;
    public DateTime? SubmittedAtUtc { get; set; }
    public Guid? SubmittedByUserId { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public DateTime? LockedAtUtc { get; set; }
    public Guid? LockedByUserId { get; set; }
    public int Version { get; set; } = 1;
    public int ConcurrencyVersion { get; set; } = 1;
    public Tenant? Tenant { get; set; }
    public Employee? Employee { get; set; }
    public TaxDeclarationCycle? Cycle { get; set; }
    public ICollection<EmployeeTaxDeclarationLine> Lines { get; set; } = new List<EmployeeTaxDeclarationLine>();
    public ICollection<TaxDeclarationAuditEvent> AuditEvents { get; set; } = new List<TaxDeclarationAuditEvent>();
}

public sealed class EmployeeTaxDeclarationLine : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid EmployeeTaxDeclarationId { get; set; }
    public Guid TaxDeclarationCategoryId { get; set; }
    public Guid TaxDeclarationItemId { get; set; }
    public decimal DeclaredAmount { get; set; }
    public decimal? ApprovedAmount { get; set; }
    public string? ReferenceNumber { get; set; }
    public DateOnly? DeclarationDate { get; set; }
    public string? Notes { get; set; }
    public EmployeeTaxDeclarationLineStatus Status { get; set; } = EmployeeTaxDeclarationLineStatus.Draft;
    public string? ReviewerComment { get; set; }
    public Tenant? Tenant { get; set; }
    public EmployeeTaxDeclaration? Declaration { get; set; }
    public TaxDeclarationCategory? Category { get; set; }
    public TaxDeclarationItem? Item { get; set; }
    public ICollection<TaxDeclarationProof> Proofs { get; set; } = new List<TaxDeclarationProof>();
}

public sealed class TaxDeclarationProof : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid EmployeeTaxDeclarationLineId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string StorageReference { get; set; } = string.Empty;
    public Guid UploadedByUserId { get; set; }
    public DateTime UploadedAtUtc { get; set; }
    public string? DocumentType { get; set; }
    public TaxDeclarationProofStatus Status { get; set; } = TaxDeclarationProofStatus.Submitted;
    public Guid? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public string? ReviewerComment { get; set; }
    public string? Hash { get; set; }
    public Tenant? Tenant { get; set; }
    public EmployeeTaxDeclarationLine? Line { get; set; }
}

public sealed class TaxDeclarationAuditEvent : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid EmployeeTaxDeclarationId { get; set; }
    public Guid? EmployeeTaxDeclarationLineId { get; set; }
    public TaxDeclarationAuditAction Action { get; set; }
    public Guid? ActorUserId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string? Comment { get; set; }
    public Tenant? Tenant { get; set; }
    public EmployeeTaxDeclaration? Declaration { get; set; }
}
