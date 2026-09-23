using HRMS.Domain.Enums;

namespace HRMS.Application.DTOs.Payroll;

public sealed record StatutoryFilingDefinitionDto(Guid Id, string Code, string Name, string FilingType, string? JurisdictionCode, bool RequiresApproval, StatutoryFilingDestinationType DestinationType, bool IsActive, Guid? ConnectionProfileId);
public sealed record StatutoryFilingConnectionProfileDto(Guid Id, string Name, StatutoryFilingConnectorType ConnectorType, string? Endpoint, bool IsActive, DateOnly EffectiveFrom, DateOnly? EffectiveTo, string? LastValidationStatus, DateTime? LastValidatedAtUtc, bool HasSecretReference);
public sealed record StatutoryFilingRunDto(Guid Id, Guid DefinitionId, string FilingPeriod, StatutoryFilingRunStatus Status, int RowCount, int ValidationErrorCount, int ValidationWarningCount, Guid? PackageId, string? PackageHash, Guid? SubmissionId, string? ExternalReference);
public sealed record StatutoryFilingIssueDto(Guid Id, StatutoryFilingIssueSeverity Severity, string Code, string Message);
public sealed record StatutoryFilingPackageDto(Guid Id, int Version, string FileName, string ContentType, string PackageHash, int RowCount, long ByteCount);
public sealed record StatutoryFilingSubmissionDto(Guid Id, StatutoryFilingSubmissionOutcome Outcome, string? ExternalReference, DateTime? SubmittedAtUtc, DateTime? AcknowledgedAtUtc);
public sealed class StatutoryFilingDefinitionRequest { public string Code { get; set; } = string.Empty; public string Name { get; set; } = string.Empty; public string FilingType { get; set; } = string.Empty; public string? JurisdictionCode { get; set; } public bool RequiresApproval { get; set; } = true; public StatutoryFilingDestinationType DestinationType { get; set; } = StatutoryFilingDestinationType.ManualDownload; public StatutoryFilingOutputFormat OutputFormat { get; set; } = StatutoryFilingOutputFormat.Csv; public string? SourceType { get; set; } public Guid? ConnectionProfileId { get; set; } }
public sealed class StatutoryFilingRunRequest { public Guid DefinitionId { get; set; } public string FilingPeriod { get; set; } = string.Empty; }
public sealed class StatutoryFilingAcknowledgementRequest { public string ReferenceNumber { get; set; } = string.Empty; public string? Notes { get; set; } }
public sealed class StatutoryFilingConnectionProfileRequest { public string Name { get; set; } = string.Empty; public StatutoryFilingConnectorType ConnectorType { get; set; } = StatutoryFilingConnectorType.ManualDownload; public string? Endpoint { get; set; } public string? NonSecretConfigurationJson { get; set; } public string? SecretReference { get; set; } public DateOnly EffectiveFrom { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow); public DateOnly? EffectiveTo { get; set; } public bool IsActive { get; set; } = true; }
