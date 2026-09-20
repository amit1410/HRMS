using HRMS.Application.Common;
using HRMS.Domain.Enums;

namespace HRMS.Application.DTOs.Payroll;

public sealed class BankAdviceQuery : PagedQuery
{
    public BankAdviceStatus? Status { get; set; }
}

public sealed record BankAdvicePaymentDto(
    Guid Id, Guid EmployeeId, string EmployeeCode, string EmployeeName, decimal NetPay,
    string CurrencyCode, BankAdvicePaymentStatus PaymentStatus, BankAdviceValidationStatus ValidationStatus,
    string? ValidationMessage, int Sequence, string PaymentReference, string AccountHolderName,
    string BankName, string MaskedAccountNumber, string? IfscCode, string? BranchName);

public sealed record BankAdviceHistoryDto(Guid Id, BankAdviceHistoryChangeType ChangeType, DateTime ChangedAtUtc, string? Message);

public sealed record BankAdviceBatchDto(
    Guid Id, Guid PayrollRunId, Guid PayrollPeriodId, string BatchNumber, DateOnly BatchDate, DateOnly PayDate,
    string CurrencyCode, BankAdviceStatus Status, int TotalEmployees, decimal TotalAmount,
    DateTime? GeneratedAtUtc, DateTime? ApprovedAtUtc, DateTime? ExportedAtUtc,
    IReadOnlyList<BankAdvicePaymentDto> Payments, IReadOnlyList<BankAdviceHistoryDto> History);
