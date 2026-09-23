namespace HRMS.Domain.Enums;

public enum StatutoryFilingRunStatus { Draft, Generated, Validated, SubmittedForApproval, Approved, ReadyToSubmit, Submitting, Submitted, Acknowledged, Rejected, Failed, Cancelled, Closed }
public enum StatutoryFilingOutputFormat { Csv, Json, FixedWidth }
public enum StatutoryFilingDestinationType { ManualDownload, HttpApi, Sftp }
public enum StatutoryFilingConnectorType { ManualDownload, Test }
public enum StatutoryFilingIssueSeverity { Error, Warning, Info }
public enum StatutoryFilingSubmissionOutcome { Accepted, Acknowledged, Pending, Rejected, Failed, Unknown }
public enum StatutoryFilingHistoryEvent { DefinitionCreated, DefinitionUpdated, RunCreated, Generated, Validated, SubmittedForApproval, Approved, PackageCreated, Submitted, Retry, Acknowledged, Rejected, Cancelled, Resubmitted, StatusRefreshed, Closed }
