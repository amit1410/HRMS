namespace HRMS.Domain.Enums;

public enum ReimbursementCategoryType { Travel, Meal, Mobile, Internet, Medical, Fuel, LocalConveyance, Relocation, Education, Other }
public enum ReimbursementTaxTreatment { NonTaxable, Taxable, PartiallyTaxable, InformationalOnly }
public enum ReimbursementSettlementMethod { Payroll, Manual, FinalSettlement }
public enum ReimbursementPolicyVersionStatus { Draft, Active, Retired }
public enum ReimbursementClaimStatus { Draft, Submitted, UnderReview, Approved, PartiallyApproved, Rejected, ReadyForSettlement, Settled, Cancelled }
public enum ReimbursementClaimLineStatus { Draft, Submitted, Approved, PartiallyApproved, Rejected, Settled, Cancelled }
public enum ReimbursementReceiptStatus { NotRequired, Missing, Uploaded, Verified, Rejected }
public enum ReimbursementHistoryEventType { DraftCreated, LineAdded, LineUpdated, LineRemoved, Submitted, UnderReview, Approved, PartiallyApproved, Rejected, ManualSettlement, PayrollSettlement, FinalSettlementSettlement, Cancelled }
public enum ReimbursementSettlementType { Payroll, Manual, FinalSettlement }
