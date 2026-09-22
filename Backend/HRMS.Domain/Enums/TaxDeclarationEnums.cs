namespace HRMS.Domain.Enums;

public enum TaxDeclarationCycleStatus { Draft, Open, ProofSubmissionOpen, ReviewInProgress, Locked, Closed, Cancelled }
public enum TaxDeclarationCategoryType { Investment, Insurance, Housing, Donation, Education, Rent, LoanInterest, Other }
public enum EmployeeTaxDeclarationStatus { Draft, Submitted, UnderReview, PartiallyApproved, Approved, Rejected, ResubmissionRequired, Locked }
public enum EmployeeTaxDeclarationLineStatus { Draft, Submitted, Approved, PartiallyApproved, Rejected, ProofRequired, ResubmissionRequired }
public enum TaxDeclarationProofStatus { Submitted, Accepted, Rejected, Replaced }
public enum TaxDeclarationAuditAction { Created, LineAdded, LineUpdated, LineDeleted, ProofUploaded, ProofReplaced, ProofAccepted, ProofRejected, Submitted, ReviewStarted, LineApproved, LinePartiallyApproved, LineRejected, ResubmissionRequested, Resubmitted, Approved, Rejected, Locked, Reopened }
