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
