using HRMS.Domain.Enums;
using HRMS.Application.Common;

namespace HRMS.Application.DTOs.Payroll;

public sealed class LoanProductRequest
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public LoanProductType ProductType { get; set; }
    public bool IsActive { get; set; } = true;
    public string CurrencyCode { get; set; } = "INR";
    public decimal? MinAmount { get; set; }
    public decimal? MaxAmount { get; set; }
    public int? MinTenureMonths { get; set; }
    public int? MaxTenureMonths { get; set; }
    public LoanInterestMethod InterestMethod { get; set; }
    public decimal? InterestRate { get; set; }
    public bool AllowPartialPrepayment { get; set; }
    public bool AllowEarlyClosure { get; set; }
    public int? MaxConcurrentLoans { get; set; }
    public LoanRecoveryPolicy RecoveryPolicy { get; set; } = LoanRecoveryPolicy.RecoverFullOrFail;
}

public sealed class LoanProductVersionRequest
{
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public decimal MinAmount { get; set; }
    public decimal MaxAmount { get; set; }
    public int MinTenureMonths { get; set; }
    public int MaxTenureMonths { get; set; }
    public LoanInterestMethod InterestMethod { get; set; }
    public decimal InterestRate { get; set; }
    public bool AllowPartialPrepayment { get; set; }
    public bool AllowEarlyClosure { get; set; }
    public LoanRecoveryPolicy RecoveryPolicy { get; set; } = LoanRecoveryPolicy.RecoverFullOrFail;
}

public sealed class LoanRequest
{
    public Guid EmployeeId { get; set; }
    public Guid LoanProductId { get; set; }
    public decimal RequestedAmount { get; set; }
    public int RequestedTenureMonths { get; set; }
    public string CurrencyCode { get; set; } = "INR";
}


public sealed class LoanRepaymentRequest
{
    public decimal Amount { get; set; }
    public DateOnly? PaymentDate { get; set; }
    public string? Reference { get; set; }
}

public sealed record LoanProductDto(Guid Id, string Code, string Name, LoanProductType ProductType, bool IsActive, string CurrencyCode, decimal? MinAmount, decimal? MaxAmount, int? MinTenureMonths, int? MaxTenureMonths, LoanInterestMethod InterestMethod, decimal? InterestRate, LoanRecoveryPolicy RecoveryPolicy);
public sealed record LoanInstallmentDto(Guid Id, int InstallmentNumber, DateOnly DueDate, decimal OpeningPrincipal, decimal PrincipalAmount, decimal InterestAmount, decimal InstallmentAmount, decimal ClosingPrincipal, LoanInstallmentStatus Status, decimal RecoveredAmount);
public sealed record EmployeeLoanDto(Guid Id, Guid EmployeeId, Guid LoanProductId, string LoanNumber, decimal RequestedAmount, decimal? ApprovedAmount, decimal? DisbursedAmount, int RequestedTenureMonths, int? ApprovedTenureMonths, LoanStatus Status, decimal OutstandingPrincipal, decimal OutstandingInterest, decimal OutstandingTotal, string CurrencyCode, IReadOnlyList<LoanInstallmentDto> Installments);
public sealed record LoanRegisterRowDto(Guid Id, string LoanNumber, Guid EmployeeId, string EmployeeCode, string EmployeeName, string ProductCode, LoanProductType ProductType, decimal ApprovedAmount, decimal PrincipalRecovered, decimal InterestRecovered, decimal TotalRecovered, decimal OutstandingPrincipal, decimal OutstandingInterest, decimal OutstandingTotal, DateOnly? NextDueDate, LoanStatus Status);
public sealed class LoanRegisterQuery : PagedQuery
{
    public Guid? ProductId { get; set; }
    public LoanProductType? ProductType { get; set; }
    public Guid? EmployeeId { get; set; }
    public LoanStatus? Status { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
}
