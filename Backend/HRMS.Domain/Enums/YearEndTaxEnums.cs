namespace HRMS.Domain.Enums;

public enum YearEndTaxRunStatus { Draft, Calculated, Submitted, Approved, Closed, Cancelled }
public enum YearEndTaxEmployeeStatus { Calculated, BlockingIssue, AdjustmentRecommended, Reconciled }
public enum YearEndTaxPreviousEmployerStatus { Draft, Submitted, Approved, Rejected }
public enum YearEndTaxAdjustmentStatus { Recommended, HandoffRequested, HandoffCreated, Resolved, Cancelled }
public enum YearEndTaxHistoryEvent { Created, Calculated, Recalculated, Submitted, Approved, Rejected, AdjustmentGenerated, AdjustmentHandedOff, Closed, Cancelled, PreviousEmployerAdded, PreviousEmployerUpdated }
