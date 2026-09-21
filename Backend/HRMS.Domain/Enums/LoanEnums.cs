namespace HRMS.Domain.Enums;

public enum LoanProductType { Loan, SalaryAdvance }
public enum LoanInterestMethod { None, Flat, ReducingBalance }
public enum LoanInterestRateType { Fixed, Configured }
public enum LoanStatus { Draft, Submitted, Approved, Rejected, Disbursed, Active, Closed, Cancelled }
public enum LoanProductVersionStatus { Draft, Active, Retired }
public enum LoanInstallmentStatus { Scheduled, PartiallyRecovered, Recovered, Deferred, Cancelled }
public enum LoanRecoveryPolicy { RecoverFullOrFail, PartialRecovery, DeferInstallment }
public enum LoanRepaymentType { Payroll, Manual, PartialPrepayment, EarlyClosure, FinalSettlement }
public enum LoanHistoryEventType { Created, Submitted, Approved, Rejected, Disbursed, Activated, ScheduleGenerated, PayrollRecovery, ManualRepayment, PartialPrepayment, EarlyClosure, FinalSettlementRecovery, Cancelled, Closed }
