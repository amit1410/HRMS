using HRMS.Domain.Common;
using HRMS.Domain.Enums;

namespace HRMS.Domain.Entities;

public sealed class BankAdviceBatch : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid PayrollRunId { get; set; }
    public Guid PayrollPeriodId { get; set; }
    public string BatchNumber { get; set; } = string.Empty;
    public DateOnly BatchDate { get; set; }
    public DateOnly PayDate { get; set; }
    public string CurrencyCode { get; set; } = "INR";
    public BankAdviceStatus Status { get; set; } = BankAdviceStatus.Draft;
    public int TotalEmployees { get; set; }
    public decimal TotalAmount { get; set; }
    public DateTime? GeneratedAtUtc { get; set; }
    public Guid? GeneratedByUserId { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTime? ExportedAtUtc { get; set; }
    public Guid? ExportedByUserId { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public Guid? CancelledByUserId { get; set; }
    public string? Notes { get; set; }
    public int Version { get; set; } = 1;
    public int ConcurrencyVersion { get; set; } = 1;
    public Tenant? Tenant { get; set; }
    public PayrollRun? PayrollRun { get; set; }
    public PayrollPeriod? PayrollPeriod { get; set; }
    public ICollection<BankAdvicePayment> Payments { get; set; } = new List<BankAdvicePayment>();
    public ICollection<BankAdviceHistory> History { get; set; } = new List<BankAdviceHistory>();
}

public sealed class BankAdvicePayment : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid BankAdviceBatchId { get; set; }
    public Guid PayrollResultId { get; set; }
    public Guid EmployeeId { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public decimal NetPay { get; set; }
    public string CurrencyCode { get; set; } = "INR";
    public BankAdvicePaymentStatus PaymentStatus { get; set; } = BankAdvicePaymentStatus.Pending;
    public BankAdviceValidationStatus ValidationStatus { get; set; } = BankAdviceValidationStatus.Pending;
    public string? ValidationMessage { get; set; }
    public int Sequence { get; set; }
    public string PaymentReference { get; set; } = string.Empty;
    public string AccountHolderName { get; set; } = string.Empty;
    public string BankName { get; set; } = string.Empty;
    public string MaskedAccountNumber { get; set; } = string.Empty;
    public string? IfscCode { get; set; }
    public string? BranchName { get; set; }
    public Tenant? Tenant { get; set; }
    public BankAdviceBatch? Batch { get; set; }
    public PayrollResult? PayrollResult { get; set; }
    public Employee? Employee { get; set; }
}

public sealed class BankAdviceHistory : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid BankAdviceBatchId { get; set; }
    public BankAdviceHistoryChangeType ChangeType { get; set; }
    public DateTime ChangedAtUtc { get; set; }
    public Guid? ActorUserId { get; set; }
    public string? Message { get; set; }
    public string SnapshotJson { get; set; } = "{}";
    public Tenant? Tenant { get; set; }
    public BankAdviceBatch? Batch { get; set; }
}
