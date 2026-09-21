using HRMS.Application.Common;
using HRMS.Domain.Enums;

namespace HRMS.Application.DTOs.Payroll;

public sealed class ReimbursementCategoryRequest
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public ReimbursementCategoryType CategoryType { get; set; }
    public bool IsActive { get; set; } = true;
    public string CurrencyCode { get; set; } = "INR";
    public bool RequiresReceipt { get; set; }
    public bool AllowsMultipleLines { get; set; } = true;
    public ReimbursementTaxTreatment TaxTreatment { get; set; }
    public ReimbursementSettlementMethod DefaultSettlementMethod { get; set; } = ReimbursementSettlementMethod.Payroll;
    public bool RequiresApproval { get; set; } = true;
    public int SettlementPriority { get; set; }
}

public sealed class ReimbursementPolicyVersionRequest
{
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public decimal? MinClaimAmount { get; set; }
    public decimal? MaxClaimAmount { get; set; }
    public decimal? PerTransactionLimit { get; set; }
    public decimal? MonthlyLimit { get; set; }
    public decimal? YearlyLimit { get; set; }
    public bool RequiresReceipt { get; set; }
    public decimal? ReceiptRequiredAbove { get; set; }
    public ReimbursementTaxTreatment TaxTreatment { get; set; }
    public ReimbursementSettlementMethod SettlementMethod { get; set; } = ReimbursementSettlementMethod.Payroll;
    public ReimbursementPolicyVersionStatus Status { get; set; } = ReimbursementPolicyVersionStatus.Active;
    public string? EligibilityJson { get; set; }
}

public sealed class ReimbursementClaimLineRequest
{
    public Guid ReimbursementCategoryId { get; set; }
    public DateOnly ExpenseDate { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal ClaimedAmount { get; set; }
    public string? MerchantName { get; set; }
    public string? ReferenceNumber { get; set; }
}

public sealed class ReimbursementClaimRequest
{
    public Guid EmployeeId { get; set; }
    public DateOnly ClaimDate { get; set; }
    public DateOnly? ExpenseFromDate { get; set; }
    public DateOnly? ExpenseToDate { get; set; }
    public string CurrencyCode { get; set; } = "INR";
    public ReimbursementSettlementMethod? SettlementMethod { get; set; }
    public List<ReimbursementClaimLineRequest> Lines { get; set; } = [];
}

public sealed class ReimbursementApprovalLineRequest
{
    public Guid ClaimLineId { get; set; }
    public decimal ApprovedAmount { get; set; }
    public string? Comment { get; set; }
    public decimal? TaxableAmount { get; set; }
}

public sealed class ReimbursementApprovalRequest
{
    public List<ReimbursementApprovalLineRequest> Lines { get; set; } = [];
    public string? Comment { get; set; }
}

public sealed class ReimbursementSettlementRequest
{
    public decimal? Amount { get; set; }
    public DateOnly? SettlementDate { get; set; }
    public string? Reference { get; set; }
}

public sealed class ReimbursementAttachmentRequest
{
    public Guid? ClaimLineId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string StorageReference { get; set; } = string.Empty;
    public long FileSize { get; set; }
}

public sealed class ReimbursementClaimQuery : PagedQuery
{
    public Guid? EmployeeId { get; set; }
    public Guid? CategoryId { get; set; }
    public ReimbursementClaimStatus? Status { get; set; }
    public ReimbursementSettlementMethod? SettlementMethod { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
}

public sealed record ReimbursementCategoryDto(Guid Id, string Code, string Name, ReimbursementCategoryType CategoryType, bool IsActive, string CurrencyCode, bool RequiresReceipt, bool AllowsMultipleLines, ReimbursementTaxTreatment TaxTreatment, ReimbursementSettlementMethod DefaultSettlementMethod, bool RequiresApproval, int SettlementPriority, IReadOnlyList<ReimbursementPolicyVersionDto> Versions);
public sealed record ReimbursementPolicyVersionDto(Guid Id, Guid CategoryId, DateOnly EffectiveFrom, DateOnly? EffectiveTo, decimal? MinClaimAmount, decimal? MaxClaimAmount, decimal? PerTransactionLimit, decimal? MonthlyLimit, decimal? YearlyLimit, bool RequiresReceipt, decimal? ReceiptRequiredAbove, ReimbursementTaxTreatment TaxTreatment, ReimbursementSettlementMethod SettlementMethod, ReimbursementPolicyVersionStatus Status);
public sealed record ReimbursementAttachmentDto(Guid Id, Guid? ClaimLineId, string FileName, string ContentType, string StorageReference, long FileSize, DateTime UploadedAtUtc);
public sealed record ReimbursementClaimLineDto(Guid Id, Guid CategoryId, string CategoryCode, Guid? PolicyVersionId, DateOnly ExpenseDate, string Description, decimal ClaimedAmount, decimal EligibleAmount, decimal ApprovedAmount, decimal TaxableAmount, decimal NonTaxableAmount, string? MerchantName, string? ReferenceNumber, bool ReceiptRequired, ReimbursementReceiptStatus ReceiptStatus, ReimbursementClaimLineStatus Status, string? ApprovalComment);
public sealed record ReimbursementSettlementDto(Guid Id, Guid? ClaimLineId, ReimbursementSettlementType SettlementType, decimal Amount, decimal TaxableAmount, decimal NonTaxableAmount, DateOnly SettlementDate, Guid? PayrollRunId, Guid? PayrollResultId, Guid? FinalSettlementId, string? Reference);
public sealed record ReimbursementHistoryDto(Guid Id, ReimbursementHistoryEventType EventType, ReimbursementClaimStatus? PreviousStatus, ReimbursementClaimStatus? NewStatus, decimal? ClaimedAmount, decimal? ApprovedAmount, string? Reason, Guid? ActorUserId, DateTime OccurredAtUtc);
public sealed record ReimbursementClaimDto(Guid Id, Guid EmployeeId, string ClaimNumber, DateOnly ClaimDate, string CurrencyCode, decimal TotalClaimedAmount, decimal TotalEligibleAmount, decimal TotalApprovedAmount, decimal TaxableAmount, decimal NonTaxableAmount, decimal SettledAmount, ReimbursementSettlementMethod SettlementMethod, ReimbursementClaimStatus Status, IReadOnlyList<ReimbursementClaimLineDto> Lines, IReadOnlyList<ReimbursementAttachmentDto> Attachments, IReadOnlyList<ReimbursementSettlementDto> Settlements, IReadOnlyList<ReimbursementHistoryDto> History);
public sealed record ReimbursementRegisterRowDto(Guid Id, string ClaimNumber, Guid EmployeeId, string EmployeeCode, string EmployeeName, decimal ClaimedAmount, decimal EligibleAmount, decimal ApprovedAmount, decimal TaxableAmount, decimal NonTaxableAmount, decimal SettledAmount, decimal OutstandingAmount, ReimbursementClaimStatus Status, ReimbursementSettlementMethod SettlementMethod, DateOnly ClaimDate);
public sealed record ReimbursementPayrollRecovery(Guid ClaimId, Guid ClaimLineId, Guid EmployeeId, string ClaimNumber, string CategoryCode, decimal ApprovedAmount, decimal TaxableAmount, decimal NonTaxableAmount, ReimbursementSettlementMethod SettlementMethod);
