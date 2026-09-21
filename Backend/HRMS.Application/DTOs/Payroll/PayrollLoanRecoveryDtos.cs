using HRMS.Domain.Enums;

namespace HRMS.Application.DTOs.Payroll;

public sealed record LoanRecoveryDue(Guid EmployeeLoanId, Guid LoanInstallmentId, string LoanNumber, string ProductCode, int InstallmentNumber, decimal PrincipalDue, decimal InterestDue, decimal TotalDue, int RecoveryPriority, LoanRecoveryPolicy RecoveryPolicy, DateOnly DueDate);
