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
public enum StatutoryConfigurationStatus { Draft, Active, Retired }
public enum StatutoryProfileChangeType { Created, Updated, Activated, Deactivated }
public enum StatutoryConfigurationChangeType { Created, VersionAdded, Activated, Deactivated }
public enum StatutoryApplicabilityStatus { Applicable, NotApplicable, NotConfigured, ConfigurationAmbiguity }
public enum PayrollCalculationHistoryChangeType { CalculationStarted, EmployeeCalculated, EmployeeCalculationFailed, CalculationCompleted, RecalculationRequested, RecalculationCompleted, ResultsReset }
