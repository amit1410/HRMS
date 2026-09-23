using HRMS.Domain.Common;
using HRMS.Domain.Enums;

namespace HRMS.Domain.Entities;

public sealed class StatutoryFilingDefinition : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string FilingType { get; set; } = string.Empty;
    public string? JurisdictionCode { get; set; }
    public bool RequiresApproval { get; set; } = true;
    public StatutoryFilingDestinationType DestinationType { get; set; } = StatutoryFilingDestinationType.ManualDownload;
    public Guid? ConnectionProfileId { get; set; }
    public bool IsActive { get; set; } = true;
    public Tenant? Tenant { get; set; }
    public ICollection<StatutoryFilingDefinitionVersion> Versions { get; set; } = new List<StatutoryFilingDefinitionVersion>();
    public StatutoryFilingConnectionProfile? ConnectionProfile { get; set; }
}

public sealed class StatutoryFilingConnectionProfile : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public StatutoryFilingConnectorType ConnectorType { get; set; } = StatutoryFilingConnectorType.ManualDownload;
    public string? Endpoint { get; set; }
    public string? NonSecretConfigurationJson { get; set; }
    public string? SecretReference { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsActive { get; set; } = true;
    public string? LastValidationStatus { get; set; }
    public DateTime? LastValidatedAtUtc { get; set; }
    public int ConcurrencyVersion { get; set; } = 1;
    public Tenant? Tenant { get; set; }
    public ICollection<StatutoryFilingDefinition> Definitions { get; set; } = new List<StatutoryFilingDefinition>();
}

public sealed class StatutoryFilingDefinitionVersion : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid DefinitionId { get; set; }
    public int Version { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public StatutoryFilingOutputFormat OutputFormat { get; set; } = StatutoryFilingOutputFormat.Csv;
    public string? SourceType { get; set; }
    public Tenant? Tenant { get; set; }
    public StatutoryFilingDefinition? Definition { get; set; }
    public ICollection<StatutoryFilingFieldMapping> FieldMappings { get; set; } = new List<StatutoryFilingFieldMapping>();
}

public sealed class StatutoryFilingFieldMapping : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid DefinitionVersionId { get; set; }
    public string OutputFieldName { get; set; } = string.Empty;
    public string SourceField { get; set; } = string.Empty;
    public int Sequence { get; set; }
    public bool Required { get; set; }
    public string? Format { get; set; }
    public string? DefaultValue { get; set; }
    public Tenant? Tenant { get; set; }
    public StatutoryFilingDefinitionVersion? DefinitionVersion { get; set; }
}

public sealed class StatutoryFilingRun : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid DefinitionId { get; set; }
    public Guid DefinitionVersionId { get; set; }
    public string FilingPeriod { get; set; } = string.Empty;
    public StatutoryFilingRunStatus Status { get; set; } = StatutoryFilingRunStatus.Draft;
    public int RowCount { get; set; }
    public int ValidationErrorCount { get; set; }
    public int ValidationWarningCount { get; set; }
    public DateTime? GeneratedAtUtc { get; set; }
    public Guid? GeneratedByUserId { get; set; }
    public Guid? SubmittedByUserId { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public int ConcurrencyVersion { get; set; } = 1;
    public Tenant? Tenant { get; set; }
    public StatutoryFilingDefinition? Definition { get; set; }
    public StatutoryFilingDefinitionVersion? DefinitionVersion { get; set; }
    public ICollection<StatutoryFilingRunItem> Items { get; set; } = new List<StatutoryFilingRunItem>();
    public ICollection<StatutoryFilingValidationIssue> ValidationIssues { get; set; } = new List<StatutoryFilingValidationIssue>();
    public ICollection<StatutoryFilingPackage> Packages { get; set; } = new List<StatutoryFilingPackage>();
    public ICollection<StatutoryFilingSubmission> Submissions { get; set; } = new List<StatutoryFilingSubmission>();
    public ICollection<StatutoryFilingHistory> History { get; set; } = new List<StatutoryFilingHistory>();
}

public sealed class StatutoryFilingRunItem : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid RunId { get; set; }
    public Guid? EmployeeId { get; set; }
    public Guid? PayrollStatutoryReturnEmployeeId { get; set; }
    public string SourceReference { get; set; } = string.Empty;
    public string SourceSnapshotJson { get; set; } = "{}";
    public decimal Amount { get; set; }
    public int Sequence { get; set; }
    public Tenant? Tenant { get; set; }
    public StatutoryFilingRun? Run { get; set; }
}

public sealed class StatutoryFilingValidationIssue : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid RunId { get; set; }
    public Guid? RunItemId { get; set; }
    public StatutoryFilingIssueSeverity Severity { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public Tenant? Tenant { get; set; }
    public StatutoryFilingRun? Run { get; set; }
    public StatutoryFilingRunItem? RunItem { get; set; }
}

public sealed class StatutoryFilingPackage : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid RunId { get; set; }
    public int Version { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "text/csv; charset=utf-8";
    public string PackageHash { get; set; } = string.Empty;
    public int FileCount { get; set; } = 1;
    public int RowCount { get; set; }
    public long ByteCount { get; set; }
    public string Content { get; set; } = string.Empty;
    public bool IsSubmitted { get; set; }
    public Guid? PreviousPackageId { get; set; }
    public Guid? ResubmissionOfSubmissionId { get; set; }
    public Tenant? Tenant { get; set; }
    public StatutoryFilingRun? Run { get; set; }
    public ICollection<StatutoryFilingSubmission> Submissions { get; set; } = new List<StatutoryFilingSubmission>();
    public StatutoryFilingPackage? PreviousPackage { get; set; }
    public StatutoryFilingSubmission? ResubmissionOfSubmission { get; set; }
}

public sealed class StatutoryFilingSubmission : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid RunId { get; set; }
    public Guid PackageId { get; set; }
    public StatutoryFilingSubmissionOutcome Outcome { get; set; } = StatutoryFilingSubmissionOutcome.Pending;
    public string? ExternalReference { get; set; }
    public string? ResponseCode { get; set; }
    public string? SafeResponseSummary { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }
    public DateTime? AcknowledgedAtUtc { get; set; }
    public Guid? ResubmissionOfSubmissionId { get; set; }
    public Tenant? Tenant { get; set; }
    public StatutoryFilingRun? Run { get; set; }
    public StatutoryFilingPackage? Package { get; set; }
    public ICollection<StatutoryFilingSubmissionAttempt> Attempts { get; set; } = new List<StatutoryFilingSubmissionAttempt>();
    public ICollection<StatutoryFilingAcknowledgement> Acknowledgements { get; set; } = new List<StatutoryFilingAcknowledgement>();
    public StatutoryFilingSubmission? ResubmissionOfSubmission { get; set; }
}

public sealed class StatutoryFilingSubmissionAttempt : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid SubmissionId { get; set; }
    public int AttemptNumber { get; set; }
    public StatutoryFilingSubmissionOutcome Outcome { get; set; }
    public string? ExternalReference { get; set; }
    public string? ResponseCode { get; set; }
    public string? SafeResponseSummary { get; set; }
    public DateTime AttemptedAtUtc { get; set; }
    public Tenant? Tenant { get; set; }
    public StatutoryFilingSubmission? Submission { get; set; }
}

public sealed class StatutoryFilingAcknowledgement : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid SubmissionId { get; set; }
    public string ReferenceNumber { get; set; } = string.Empty;
    public DateTime AcknowledgedAtUtc { get; set; }
    public string? Notes { get; set; }
    public Tenant? Tenant { get; set; }
    public StatutoryFilingSubmission? Submission { get; set; }
}

public sealed class StatutoryFilingHistory : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid RunId { get; set; }
    public StatutoryFilingHistoryEvent Event { get; set; }
    public StatutoryFilingRunStatus? PreviousStatus { get; set; }
    public StatutoryFilingRunStatus? NewStatus { get; set; }
    public Guid? ActorUserId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public string? Message { get; set; }
    public Tenant? Tenant { get; set; }
    public StatutoryFilingRun? Run { get; set; }
}
