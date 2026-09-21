namespace HRMS.Domain.Enums;

public enum SeparationReason { Resignation, Retirement, Termination, Redundancy, Death, Disability, ContractEnd, Other }
public enum GratuityPolicyVersionStatus { Draft, Published, Retired }
public enum GratuityFormulaType { ServiceDaysBased, ServiceMonthsBased, ServiceYearsBased, FixedAmount, CustomConfigured }
public enum GratuityWageBasisType { Basic, BasicPlusDA, Gross, FixedConfiguredAmount, SelectedSalaryComponents }
public enum ServiceRoundingMethod { CompletedYearsOnly, RoundRemainingMonthsUpAtThreshold, ExactMonths, ExactDays, NoRounding }
public enum SeparationBenefitTaxTreatment { Taxable, NonTaxable, PartiallyTaxable }
public enum SeparationBenefitCalculationStatus { Preview, Calculated, Finalized, Cancelled }
public enum NoticeSettlementType { None, NoticePay, NoticeRecovery }
public enum SeparationBenefitHistoryEventType { PolicyCreated, PolicyVersionCreated, EligibilityEvaluated, GratuityCalculated, LeaveEncashmentCalculated, NoticeCalculated, OverrideRequested, OverrideApproved, FinalSettlementIncluded, Finalized, Cancelled }
