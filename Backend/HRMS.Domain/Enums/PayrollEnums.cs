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
