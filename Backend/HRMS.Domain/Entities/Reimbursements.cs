using HRMS.Domain.Common;
using HRMS.Domain.Enums;

namespace HRMS.Domain.Entities;

public sealed class ReimbursementCategory : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public ReimbursementCategoryType CategoryType { get; set; }
    public bool IsActive { get; set; } = true;
    public string CurrencyCode { get; set; } = "INR";
    public bool RequiresReceipt { get; set; }
    public bool AllowsMultipleLines { get; set; } = true;
    public ReimbursementTaxTreatment TaxTreatment { get; set; } = ReimbursementTaxTreatment.NonTaxable;
    public ReimbursementSettlementMethod DefaultSettlementMethod { get; set; } = ReimbursementSettlementMethod.Payroll;
    public bool RequiresApproval { get; set; } = true;
    public int SettlementPriority { get; set; }
    public Tenant? Tenant { get; set; }
    public ICollection<ReimbursementPolicyVersion> Versions { get; set; } = new List<ReimbursementPolicyVersion>();
}

public sealed class ReimbursementPolicyVersion : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid ReimbursementCategoryId { get; set; }
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
    public ReimbursementSettlementMethod SettlementMethod { get; set; }
    public ReimbursementPolicyVersionStatus Status { get; set; } = ReimbursementPolicyVersionStatus.Active;
    public string? EligibilityJson { get; set; }
    public Tenant? Tenant { get; set; }
    public ReimbursementCategory? Category { get; set; }
    public ICollection<ReimbursementClaimLine> ClaimLines { get; set; } = new List<ReimbursementClaimLine>();
}

public sealed class ReimbursementClaim : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public string ClaimNumber { get; set; } = string.Empty;
    public DateOnly ClaimDate { get; set; }
    public DateOnly? ExpenseFromDate { get; set; }
    public DateOnly? ExpenseToDate { get; set; }
    public string CurrencyCode { get; set; } = "INR";
    public decimal TotalClaimedAmount { get; set; }
    public decimal TotalEligibleAmount { get; set; }
    public decimal TotalApprovedAmount { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal NonTaxableAmount { get; set; }
    public decimal SettledAmount { get; set; }
    public ReimbursementSettlementMethod SettlementMethod { get; set; } = ReimbursementSettlementMethod.Payroll;
    public ReimbursementClaimStatus Status { get; set; } = ReimbursementClaimStatus.Draft;
    public DateTime? SubmittedAtUtc { get; set; }
    public Guid? SubmittedByUserId { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTime? RejectedAtUtc { get; set; }
    public Guid? RejectedByUserId { get; set; }
    public string? RejectionReason { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public Guid? CancelledByUserId { get; set; }
    public string? CancellationReason { get; set; }
    public DateTime? SettledAtUtc { get; set; }
    public Guid? SettledByUserId { get; set; }
    public string? SettlementReference { get; set; }
    public int ConcurrencyVersion { get; set; } = 1;
    public Tenant? Tenant { get; set; }
    public Employee? Employee { get; set; }
    public ICollection<ReimbursementClaimLine> Lines { get; set; } = new List<ReimbursementClaimLine>();
    public ICollection<ReimbursementAttachment> Attachments { get; set; } = new List<ReimbursementAttachment>();
    public ICollection<ReimbursementSettlement> Settlements { get; set; } = new List<ReimbursementSettlement>();
    public ICollection<ReimbursementHistory> History { get; set; } = new List<ReimbursementHistory>();
}

public sealed class ReimbursementClaimLine : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid ReimbursementClaimId { get; set; }
    public Guid ReimbursementCategoryId { get; set; }
    public Guid? PolicyVersionId { get; set; }
    public DateOnly ExpenseDate { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal ClaimedAmount { get; set; }
    public decimal EligibleAmount { get; set; }
    public decimal ApprovedAmount { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal NonTaxableAmount { get; set; }
    public string? MerchantName { get; set; }
    public string? ReferenceNumber { get; set; }
    public bool ReceiptRequired { get; set; }
    public ReimbursementReceiptStatus ReceiptStatus { get; set; } = ReimbursementReceiptStatus.NotRequired;
    public string? ApprovalComment { get; set; }
    public ReimbursementClaimLineStatus Status { get; set; } = ReimbursementClaimLineStatus.Draft;
    public Tenant? Tenant { get; set; }
    public ReimbursementClaim? Claim { get; set; }
    public ReimbursementCategory? Category { get; set; }
    public ReimbursementPolicyVersion? PolicyVersion { get; set; }
    public ICollection<ReimbursementAttachment> Attachments { get; set; } = new List<ReimbursementAttachment>();
}

public sealed class ReimbursementAttachment : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid ReimbursementClaimId { get; set; }
    public Guid? ReimbursementClaimLineId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public string StorageReference { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public DateTime UploadedAtUtc { get; set; }
    public Guid? UploadedByUserId { get; set; }
    public Tenant? Tenant { get; set; }
    public ReimbursementClaim? Claim { get; set; }
    public ReimbursementClaimLine? ClaimLine { get; set; }
}

public sealed class ReimbursementSettlement : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid ReimbursementClaimId { get; set; }
    public Guid? ReimbursementClaimLineId { get; set; }
    public Guid EmployeeId { get; set; }
    public ReimbursementSettlementType SettlementType { get; set; }
    public decimal Amount { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal NonTaxableAmount { get; set; }
    public DateOnly SettlementDate { get; set; }
    public Guid? PayrollRunId { get; set; }
    public Guid? PayrollResultId { get; set; }
    public Guid? FinalSettlementId { get; set; }
    public string? Reference { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public Tenant? Tenant { get; set; }
    public ReimbursementClaim? Claim { get; set; }
    public ReimbursementClaimLine? ClaimLine { get; set; }
}

public sealed class ReimbursementHistory : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid ReimbursementClaimId { get; set; }
    public ReimbursementHistoryEventType EventType { get; set; }
    public ReimbursementClaimStatus? PreviousStatus { get; set; }
    public ReimbursementClaimStatus? NewStatus { get; set; }
    public decimal? ClaimedAmount { get; set; }
    public decimal? ApprovedAmount { get; set; }
    public string? Reason { get; set; }
    public string? SourceType { get; set; }
    public Guid? SourceId { get; set; }
    public Guid? ActorUserId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public Tenant? Tenant { get; set; }
    public ReimbursementClaim? Claim { get; set; }
}
