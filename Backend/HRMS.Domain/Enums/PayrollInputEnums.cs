namespace HRMS.Domain.Enums;

public enum PayrollInputType { OneTimeEarning, OneTimeDeduction, Adjustment, ArrearInput, RecoveryInput, InformationOnly }
public enum PayrollInputBatchStatus { Draft, Validating, ValidationFailed, Validated, Submitted, Approved, Rejected, Posting, Posted, Cancelled, Reversed }
public enum PayrollInputLineStatus { Staged, Valid, Invalid, Warning, Posted, Cancelled }
public enum PayrollInputIssueSeverity { Error, Warning }
public enum PayrollInputSourceType { Manual, Csv }
public enum PayrollInputTemplateTransform { None, Trim, Uppercase, Lowercase, Date, Decimal }
public enum PayrollInputHistoryEvent { Created, Uploaded, Parsed, Validated, ValidationFailed, Corrected, Submitted, Approved, Rejected, Posted, Cancelled, ReversalRequested }
