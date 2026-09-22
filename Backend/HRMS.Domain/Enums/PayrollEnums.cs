namespace HRMS.Domain.Enums;

public enum SalaryComponentType
{
    Earning,
    Deduction,
    EmployerContribution,
    Reimbursement,
    Information
}

public enum SalaryCalculationType
{
    FixedAmount,
    Percentage,
    Formula,
    ManualInput,
    AttendanceBased,
    LeaveBased,
    Statutory
}

public enum SalaryStatutoryType
{
    None,
    ProvidentFund,
    Esi,
    ProfessionalTax,
    LabourWelfareFund,
    IncomeTax,
    Gratuity,
    Other
}

public enum SalaryComponentChangeType
{
    Created,
    Updated,
    Activated,
    Deactivated
}

public enum SalaryStructureCalculationType
{
    FixedAmount,
    Percentage,
    Formula,
    Manual
}

public enum SalaryStructureChangeType
{
    Created,
    Updated,
    ComponentAdded,
    ComponentChanged,
    ComponentRemoved,
    Activated,
    Deactivated
}

public enum SalaryPayFrequency { Monthly, BiWeekly, Weekly, Daily }
public enum EmployeeSalaryAssignmentStatus { Active, Inactive }
public enum SalaryChangeReason { NewHire, Confirmation, Increment, Promotion, Demotion, Transfer, Correction, ContractRevision, Other }
public enum EmployeeSalaryAssignmentChangeType { Created, Updated, OverrideAdded, OverrideChanged, OverrideRemoved, EndDated, Activated, Deactivated }

public enum PayrollPeriodType { Monthly, BiWeekly, Weekly, SemiMonthly, Custom }
public enum PayrollPeriodStatus { Draft, Open, Closed, Locked }
public enum PayrollRunType { Regular, Supplementary, OffCycle }
public enum PayrollAnalyticsSnapshotType { Run, Comparison, PrePayroll, PostPayroll }
public enum PayrollControlScope { PayrollRun, Employee, Component, Department, CostCenter, Statutory, BankAdvice, Accounting }
public enum PayrollControlMetric { GrossPay, NetPay, TotalDeduction, Tax, EmployeeCount, ComponentAmount, BankAdviceTotal, GLTotal, EmployerContribution }
public enum PayrollFindingSeverity { Info, Warning, Critical }
public enum PayrollControlAction { Informational, RequireAcknowledgement, BlockApproval, BlockFinalization }
public enum PayrollReconciliationType { PrePayroll, PostPayroll }
public enum PayrollReconciliationStatus { Generated, Acknowledged, Superseded }
public enum PayrollFindingStatus { Open, Acknowledged, Resolved, AcceptedException, Superseded }
public enum PayrollAnomalyType { HighGrossVariance, HighNetVariance, HighDeductionVariance, NegativeNetPay, ZeroNetPay, PopulationMismatch, BankMismatch, AccountingMismatch, StatutoryMismatch, DuplicateSettlement, MissingConfiguration, MissingSource, UnexpectedEmployee, MissingEmployee }
public enum PayrollRunStatus { Draft, Prepared, Processing, Calculated, Approved, Finalized, Cancelled }
public enum PayrollRunEmployeeStatus { Eligible, Excluded }
public enum PayrollRunHistoryChangeType { Created, Prepared, PopulationGenerated, PopulationRebuilt, StatusChanged, Approved, Finalized, Cancelled }
public enum PayrollResultStatus { Calculated, Failed }
public enum PayslipStatus { Generated, Published, Superseded, Void }
public enum PayslipHistoryChangeType { Generated, Regenerated, Published, Downloaded, Superseded, Voided }
public enum BankAdviceStatus { Draft, Prepared, Approved, Exported, Cancelled }
public enum BankAdvicePaymentStatus { Pending, Ready, Exported, Processed, Failed, Cancelled }
public enum BankAdviceValidationStatus { Pending, Valid, Invalid }
public enum BankAdviceHistoryChangeType { Generated, ValidationFailed, Prepared, Approved, Exported, Cancelled }
public enum PayrollCalculationErrorCode { NoSalaryAssignment, NoSalaryStructureVersion, MissingBaseComponent, CircularDependency, InvalidFormula, InvalidOverride, CurrencyMismatch, NegativeNetPay, CalculationFailed, StatutoryConfigurationMissing, StatutoryConfigurationAmbiguous, InvalidStatutoryConfiguration, StatutoryBasisMissing, StatutoryCalculationFailed, JurisdictionUnsupported, EmployeeStatutoryProfileInvalid }
public enum StatutoryType { ProvidentFund, Esi, ProfessionalTax, IncomeTax }

public enum PayrollComplianceType { ProvidentFund, Esi, ProfessionalTax, IncomeTaxTds }
public enum PayrollCompliancePeriodStatus { Open, Closed, Cancelled }
public enum PayrollStatutoryReturnStatus { Draft, Generated, Validated, Approved, Exported, Filed, Cancelled }
public enum PayrollComplianceValidationStatus { Valid, Invalid, Warning }
public enum PayrollStatutoryChallanStatus { Pending, Prepared, Paid, Cancelled }
public enum PayrollStatutoryComplianceHistoryChangeType { Created, Generated, Validated, Approved, Exported, Filed, Cancelled }
public enum StatutoryConfigurationStatus { Draft, Active, Retired }
public enum StatutoryProfileChangeType { Created, Updated, Activated, Deactivated }
public enum StatutoryConfigurationChangeType { Created, VersionAdded, Activated, Deactivated }
public enum StatutoryApplicabilityStatus { Applicable, NotApplicable, NotConfigured, ConfigurationAmbiguity }
public enum PayrollCalculationHistoryChangeType { CalculationStarted, EmployeeCalculated, EmployeeCalculationFailed, CalculationCompleted, RecalculationRequested, RecalculationCompleted, ResultsReset }
public enum PayrollGLAccountType { Expense, Liability, Asset, Clearing }
public enum PayrollAccountingConfigurationVersionStatus { Draft, Active, Retired }
public enum PayrollGLMappingType
{
    Earnings,
    EmployeeDeduction,
    EmployerContribution,
    NetPayable,
    StatutoryLiability,
    LoanDisbursementReceivable,
    SalaryAdvanceDisbursementReceivable,
    LoanPayrollRecovery,
    LoanInterestRecovery,
    LoanFinalSettlementRecovery,
    ReimbursementExpense,
    ReimbursementPayable,
    TaxableReimbursementExpense,
    ManualReimbursementSettlement,
    PayrollReimbursementSettlement,
    FinalSettlementReimbursement
    ,GratuityExpense
    ,GratuityPayable
    ,LeaveEncashmentExpense
    ,NoticePayExpense
    ,NoticeRecoveryReceivable
    ,SeparationBenefitExpense
    ,FinalSettlementBenefitPayable
}
public enum PayrollJournalAggregationMode { Account, AccountAndCostCenter, EmployeeDetail }
public enum PayrollJournalStatus { Draft, Generated, Validated, Approved, Posted, Exported, Cancelled }
public enum PayrollJournalHistoryChangeType { Generated, Validated, Approved, Posted, Exported, Cancelled, Reversed }
public enum PayrollRetroTriggerType { SalaryAssignmentChange, SalaryStructureChange, ComponentChange, EmploymentChange, ManualCorrection }
public enum PayrollRetroStatus { Detected, Evaluated, Approved, Applied, Cancelled, NoImpact }
public enum PayrollAdjustmentType { ArrearEarning, ArrearDeduction, Recovery, StatutoryAdjustment, FinalSettlementAdjustment, AdditionalEarning, AdditionalDeduction, PriorPeriodCorrection, TaxAdjustment, ReimbursementCorrection, LoanCorrection, VariablePayCorrection, SeparationCorrection, ManualPayrollCorrection, OtherConfigured }
public enum PayrollAdjustmentStatus { Draft, Submitted, Approved, Rejected, Scheduled, Unapplied, Applied, PartiallyApplied, Cancelled, Reversed }
public enum PayrollAdjustmentDirection { Earning, Deduction }
public enum PayrollAdjustmentTaxTreatment { Taxable, NonTaxable, TaxAdjustmentOnly, RecalculateAccordingToPayrollRules }
public enum PayrollAdjustmentStatutoryTreatment { IncludeInPf, IncludeInEsi, IncludeInPt, IncludeInIncomeTax, ExcludeFromStatutory, RecalculateConfiguredStatutory }
public enum PayrollAdjustmentSettlementMethod { Payroll, FinalSettlement, ManualRecorded }
public enum PayrollAdjustmentHistoryEventType { Created, Submitted, Approved, Rejected, Scheduled, Applied, PartialApplication, Cancelled, Reversed, CorrectionSnapshotCaptured, ReversalRequested, ReversalApproved, ReversalProcessed, ReissueGenerated }
public enum PayrollReversalStatus { Requested, Approved, Processed, Rejected, Cancelled }
public enum FinalSettlementStatus { Draft, Calculated, Reviewed, Approved, Finalized, Cancelled }
public enum FinalSettlementLineType { UnpaidSalary, Arrear, LeaveEncashment, Bonus, Reimbursement, NoticePay, NoticeRecovery, Gratuity, Recovery, StatutoryAdjustment, Other }
public enum PayrollRetroHistoryChangeType { Created, Evaluated, Approved, Applied, Cancelled, NoImpact }
public enum FinalSettlementHistoryChangeType { Created, Calculated, Reviewed, Approved, Finalized, Cancelled }
