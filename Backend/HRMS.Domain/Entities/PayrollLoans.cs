using HRMS.Domain.Common;
using HRMS.Domain.Enums;

namespace HRMS.Domain.Entities;

public sealed class LoanProduct : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
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
    public LoanInterestMethod InterestMethod { get; set; } = LoanInterestMethod.None;
    public decimal? InterestRate { get; set; }
    public LoanInterestRateType InterestRateType { get; set; } = LoanInterestRateType.Fixed;
    public bool AllowZeroInterest { get; set; } = true;
    public bool AllowPartialPrepayment { get; set; }
    public bool AllowEarlyClosure { get; set; }
    public bool AllowTopUp { get; set; }
    public int? MaxConcurrentLoans { get; set; }
    public bool RequiresApproval { get; set; } = true;
    public int RecoveryPriority { get; set; }
    public LoanRecoveryPolicy RecoveryPolicy { get; set; } = LoanRecoveryPolicy.RecoverFullOrFail;
    public Tenant? Tenant { get; set; }
    public ICollection<LoanProductVersion> Versions { get; set; } = new List<LoanProductVersion>();
    public ICollection<EmployeeLoan> Loans { get; set; } = new List<EmployeeLoan>();
}

public sealed class LoanProductVersion : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid LoanProductId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public decimal MinAmount { get; set; }
    public decimal MaxAmount { get; set; }
    public int MinTenureMonths { get; set; }
    public int MaxTenureMonths { get; set; }
    public LoanInterestMethod InterestMethod { get; set; }
    public decimal InterestRate { get; set; }
    public LoanProductVersionStatus Status { get; set; } = LoanProductVersionStatus.Draft;
    public bool AllowPartialPrepayment { get; set; }
    public bool AllowEarlyClosure { get; set; }
    public LoanRecoveryPolicy RecoveryPolicy { get; set; } = LoanRecoveryPolicy.RecoverFullOrFail;
    public Tenant? Tenant { get; set; }
    public LoanProduct? LoanProduct { get; set; }
    public ICollection<EmployeeLoan> Loans { get; set; } = new List<EmployeeLoan>();
}

public sealed class EmployeeLoan : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid LoanProductId { get; set; }
    public Guid LoanProductVersionId { get; set; }
    public string LoanNumber { get; set; } = string.Empty;
    public decimal RequestedAmount { get; set; }
    public decimal? ApprovedAmount { get; set; }
    public decimal? DisbursedAmount { get; set; }
    public string CurrencyCode { get; set; } = "INR";
    public int RequestedTenureMonths { get; set; }
    public int? ApprovedTenureMonths { get; set; }
    public LoanInterestMethod InterestMethod { get; set; }
    public decimal InterestRate { get; set; }
    public LoanStatus Status { get; set; } = LoanStatus.Draft;
    public LoanRecoveryPolicy RecoveryPolicy { get; set; } = LoanRecoveryPolicy.RecoverFullOrFail;
    public DateTime RequestedAtUtc { get; set; }
    public Guid? RequestedByUserId { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTime? RejectedAtUtc { get; set; }
    public Guid? RejectedByUserId { get; set; }
    public string? RejectionReason { get; set; }
    public DateTime? DisbursedAtUtc { get; set; }
    public DateOnly? FirstRecoveryDate { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
    public Guid? ClosedByUserId { get; set; }
    public string? ClosureReason { get; set; }
    public decimal OutstandingPrincipal { get; set; }
    public decimal OutstandingInterest { get; set; }
    public decimal OutstandingTotal { get; set; }
    public int ConcurrencyVersion { get; set; } = 1;
    public Tenant? Tenant { get; set; }
    public Employee? Employee { get; set; }
    public LoanProduct? LoanProduct { get; set; }
    public LoanProductVersion? LoanProductVersion { get; set; }
    public ICollection<LoanInstallment> Installments { get; set; } = new List<LoanInstallment>();
    public ICollection<LoanRepayment> Repayments { get; set; } = new List<LoanRepayment>();
    public ICollection<LoanHistory> History { get; set; } = new List<LoanHistory>();
}

public sealed class LoanInstallment : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid EmployeeLoanId { get; set; }
    public int InstallmentNumber { get; set; }
    public DateOnly DueDate { get; set; }
    public decimal OpeningPrincipal { get; set; }
    public decimal PrincipalAmount { get; set; }
    public decimal InterestAmount { get; set; }
    public decimal InstallmentAmount { get; set; }
    public decimal ClosingPrincipal { get; set; }
    public LoanInstallmentStatus Status { get; set; } = LoanInstallmentStatus.Scheduled;
    public Guid? PayrollRunId { get; set; }
    public Guid? PayrollResultId { get; set; }
    public decimal RecoveredAmount { get; set; }
    public DateTime? RecoveredAtUtc { get; set; }
    public Tenant? Tenant { get; set; }
    public EmployeeLoan? EmployeeLoan { get; set; }
    public PayrollRun? PayrollRun { get; set; }
    public PayrollResult? PayrollResult { get; set; }
}

public sealed class LoanRepayment : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid EmployeeLoanId { get; set; }
    public decimal Amount { get; set; }
    public decimal PrincipalAmount { get; set; }
    public decimal InterestAmount { get; set; }
    public LoanRepaymentType RepaymentType { get; set; }
    public DateOnly PaymentDate { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public Guid? PayrollRunId { get; set; }
    public Guid? PayrollResultId { get; set; }
    public Guid? LoanInstallmentId { get; set; }
    public string? Reference { get; set; }
    public Guid CreatedByUserId { get; set; }
    public Tenant? Tenant { get; set; }
    public EmployeeLoan? EmployeeLoan { get; set; }
    public LoanInstallment? LoanInstallment { get; set; }
}

public sealed class LoanHistory : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid EmployeeLoanId { get; set; }
    public LoanHistoryEventType EventType { get; set; }
    public LoanStatus? PreviousStatus { get; set; }
    public LoanStatus? NewStatus { get; set; }
    public decimal? Amount { get; set; }
    public string? Reason { get; set; }
    public string? SourceType { get; set; }
    public Guid? SourceId { get; set; }
    public Guid? ActorUserId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public Tenant? Tenant { get; set; }
    public EmployeeLoan? EmployeeLoan { get; set; }
}
