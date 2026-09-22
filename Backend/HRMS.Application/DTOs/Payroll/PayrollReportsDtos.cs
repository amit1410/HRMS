using HRMS.Application.Common;
using HRMS.Domain.Enums;

namespace HRMS.Application.DTOs.Payroll;

public sealed class PayrollReportQuery : PagedQuery
{
    public Guid? PayrollRunId { get; set; }
    public Guid? PayrollPeriodId { get; set; }
    public Guid? EmployeeId { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid? CostCenterId { get; set; }
    public string? ComponentType { get; set; }
}

public sealed record PayrollReportRegisterRowDto(Guid PayrollRunId, Guid PayrollResultId, Guid EmployeeId, string EmployeeCode, string EmployeeName, DateOnly PeriodEndDate, PayrollRunType RunType, string Department, string CostCenter, decimal EarningsTotal, decimal GrossTotal, decimal DeductionTotal, decimal TaxTotal, decimal EmployerContributionTotal, decimal NetPay, decimal ReimbursementTotal, decimal LoanRecoveryTotal, decimal VariablePayTotal, decimal AdjustmentTotal, PayrollResultStatus Status);
public sealed record PayrollComponentReportRowDto(Guid PayrollRunId, Guid PayrollResultId, Guid EmployeeId, string EmployeeCode, string EmployeeName, string ComponentCode, string ComponentName, SalaryComponentType ComponentType, decimal Amount, bool IsEarning, bool IsDeduction, bool IsEmployerContribution);
public sealed record PayrollDimensionReportRowDto(Guid? DimensionId, string Code, string Name, int EmployeeCount, int ResultCount, decimal EarningsTotal, decimal GrossTotal, decimal DeductionTotal, decimal TaxTotal, decimal EmployerContributionTotal, decimal NetPayTotal);
public sealed record PayrollComponentSummaryRowDto(string ComponentCode, string ComponentName, SalaryComponentType ComponentType, int EmployeeCount, decimal TotalAmount, decimal AverageAmount, decimal MinimumAmount, decimal MaximumAmount);
public sealed record PayrollStatutorySummaryRowDto(StatutoryType StatutoryType, int EmployeeCount, decimal EmployeeContribution, decimal EmployerContribution, decimal TotalAmount);
public sealed record PayrollBankPaymentReportRowDto(Guid PayrollRunId, string BatchNumber, Guid EmployeeId, string EmployeeCode, string EmployeeName, decimal NetPay, string PaymentReference, BankAdvicePaymentStatus PaymentStatus, BankAdviceValidationStatus ValidationStatus);
public sealed record PayrollAccountingSummaryRowDto(Guid? PayrollRunId, string JournalNumber, decimal Debit, decimal Credit, bool IsBalanced, PayrollJournalStatus Status, int LineCount);
public sealed record PayrollLoanReportRowDto(Guid EmployeeId, string EmployeeCode, Guid EmployeeLoanId, Guid? LoanInstallmentId, Guid? PayrollRunId, decimal Amount, decimal PrincipalAmount, decimal InterestAmount, DateOnly PaymentDate, string? Reference);
public sealed record PayrollReimbursementReportRowDto(Guid EmployeeId, string EmployeeCode, Guid ReimbursementClaimId, Guid? PayrollRunId, Guid? PayrollResultId, decimal Amount, decimal TaxableAmount, decimal NonTaxableAmount, DateOnly SettlementDate, string? Reference);
public sealed record PayrollVariablePayReportRowDto(Guid EmployeeId, string EmployeeCode, Guid VariablePayAwardId, string AwardNumber, Guid? PayrollRunId, decimal Amount, decimal TaxableAmount, decimal NonTaxableAmount, DateOnly SettlementDate, string? Reference);
public sealed record PayrollAdjustmentReportRowDto(Guid PayrollAdjustmentId, Guid? PayrollRunId, Guid? PayrollResultId, Guid? FinalSettlementId, decimal AppliedAmount, DateOnly AppliedDate, PayrollAdjustmentType AdjustmentType, PayrollAdjustmentStatus Status);
public sealed record PayrollFinalSettlementReportRowDto(Guid Id, Guid EmployeeId, string EmployeeCode, string EmployeeName, DateOnly SettlementDate, FinalSettlementStatus Status, decimal GrossPayable, decimal TotalDeductions, decimal NetSettlement, decimal Gratuity, decimal LeaveEncashment, decimal NoticePay, decimal NoticeRecovery, decimal Reimbursement, decimal LoanRecovery, decimal VariablePay, decimal Adjustments);
public sealed record PayrollOffCycleReportRowDto(Guid PayrollRunId, string RunNumber, Guid EmployeeId, string EmployeeCode, string EmployeeName, decimal EarningsTotal, decimal DeductionTotal, decimal NetPay, PayrollRunStatus Status);
public sealed record PayrollDashboardDto(int PayrollRunCount, int EmployeeCount, decimal GrossTotal, decimal DeductionTotal, decimal NetPayTotal, decimal EmployerContributionTotal, decimal TaxTotal, decimal ReimbursementTotal, decimal LoanRecoveryTotal, decimal VariablePayTotal, decimal AdjustmentTotal, decimal FinalSettlementTotal, int ExceptionCount);
public sealed record PayrollTrendRowDto(Guid PayrollRunId, Guid PayrollPeriodId, DateOnly PeriodEndDate, PayrollRunType RunType, int EmployeeCount, decimal GrossTotal, decimal DeductionTotal, decimal NetPayTotal, decimal EmployerContributionTotal, decimal TaxTotal, decimal ReimbursementTotal, decimal LoanRecoveryTotal, decimal VariablePayTotal, decimal AdjustmentTotal);
