namespace HRMS.Domain.Enums;

public enum VariablePayPlanType { FixedBonus, PercentageOfSalary, TargetVariablePay, PerformanceLinked, OneTimeAward, RecurringVariableAllowance, DiscretionaryAward }
public enum VariablePayPlanVersionStatus { Draft, Published, Inactive, Superseded }
public enum VariablePayCalculationMethod { FixedAmount, PercentageOfSalary, TargetWithMultiplier, ManualAmount }
public enum VariablePaySalaryBasisType { Basic, Gross, FixedConfiguredAmount, SelectedSalaryComponents }
public enum VariablePayProrationMethod { None, CalendarDays, ServiceDays, CompletedMonths, EligibleDaysInPeriod }
public enum VariablePayEligibilityMethod { ActiveEmployment, MinimumService, ConfiguredApplicability, ManualReview }
public enum VariablePayPayoutFrequency { Monthly, Quarterly, HalfYearly, Annual, Custom }
public enum VariablePayTaxTreatment { Taxable, NonTaxable, PartiallyTaxable }
public enum VariablePayFinalSettlementTreatment { IncludeOutstanding, Exclude, ProrateAndInclude, CancelOutstanding }
public enum VariablePayAwardStatus { Draft, Calculated, Submitted, Approved, Rejected, Scheduled, PartiallyPaid, Paid, Cancelled }
public enum VariablePaySettlementType { Payroll, FinalSettlement, ManualRecorded }
public enum VariablePayHistoryEventType { AwardGenerated, AwardCalculated, AwardSubmitted, AwardApproved, AwardRejected, AwardOverridden, AwardScheduled, PayrollSettled, FinalSettlementSettled, PartiallyPaid, Cancelled }
