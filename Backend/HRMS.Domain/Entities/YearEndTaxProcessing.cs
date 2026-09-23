using HRMS.Domain.Common;
using HRMS.Domain.Enums;

namespace HRMS.Domain.Entities;

public sealed class YearEndTaxRun : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public int TaxYear { get; set; }
    public string TaxYearCode { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public YearEndTaxRunStatus Status { get; set; } = YearEndTaxRunStatus.Draft;
    public int EmployeeCount { get; set; }
    public int BlockingIssueCount { get; set; }
    public decimal TotalTaxDue { get; set; }
    public decimal TotalExcessTax { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public Guid? SubmittedByUserId { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public Guid? ClosedByUserId { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
    public Guid? CancelledByUserId { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public int ConcurrencyVersion { get; set; } = 1;
    public Tenant? Tenant { get; set; }
    public ICollection<YearEndTaxEmployee> Employees { get; set; } = new List<YearEndTaxEmployee>();
    public ICollection<YearEndTaxPreviousEmployerInput> PreviousEmployerInputs { get; set; } = new List<YearEndTaxPreviousEmployerInput>();
    public ICollection<YearEndTaxAdjustment> Adjustments { get; set; } = new List<YearEndTaxAdjustment>();
    public ICollection<YearEndTaxHistory> History { get; set; } = new List<YearEndTaxHistory>();
}

public sealed class YearEndTaxEmployee : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid RunId { get; set; }
    public Guid EmployeeId { get; set; }
    public decimal YtdGross { get; set; }
    public decimal YtdTaxableIncome { get; set; }
    public decimal YtdTaxDeducted { get; set; }
    public decimal ApprovedDeclarationAmount { get; set; }
    public decimal ApprovedProofAmount { get; set; }
    public decimal PreviousEmployerTaxableIncome { get; set; }
    public decimal PreviousEmployerTaxDeducted { get; set; }
    public decimal ProjectedRemainingTaxableIncome { get; set; }
    public decimal ProjectedAnnualTaxableIncome { get; set; }
    public decimal ProjectedAnnualTax { get; set; }
    public decimal EstimatedTaxDue { get; set; }
    public decimal EstimatedExcessTax { get; set; }
    public decimal FinalTaxableIncome { get; set; }
    public decimal FinalTaxLiability { get; set; }
    public YearEndTaxEmployeeStatus Status { get; set; } = YearEndTaxEmployeeStatus.Calculated;
    public string? BlockingIssueCode { get; set; }
    public string? BlockingIssueMessage { get; set; }
    public string CalculationSnapshotJson { get; set; } = "{}";
    public int ConcurrencyVersion { get; set; } = 1;
    public Tenant? Tenant { get; set; }
    public YearEndTaxRun? Run { get; set; }
    public Employee? Employee { get; set; }
    public ICollection<YearEndTaxStatement> Statements { get; set; } = new List<YearEndTaxStatement>();
    public ICollection<YearEndTaxAdjustment> Adjustments { get; set; } = new List<YearEndTaxAdjustment>();
}

public sealed class YearEndTaxPreviousEmployerInput : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid RunId { get; set; }
    public Guid EmployeeId { get; set; }
    public string EmployerName { get; set; } = string.Empty;
    public string? EmployerReference { get; set; }
    public decimal TaxableIncome { get; set; }
    public decimal TaxDeducted { get; set; }
    public decimal EligibleDeductionAmount { get; set; }
    public string? EvidenceReference { get; set; }
    public YearEndTaxPreviousEmployerStatus Status { get; set; } = YearEndTaxPreviousEmployerStatus.Draft;
    public Guid? CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public int ConcurrencyVersion { get; set; } = 1;
    public Tenant? Tenant { get; set; }
    public YearEndTaxRun? Run { get; set; }
    public Employee? Employee { get; set; }
}

public sealed class YearEndTaxAdjustment : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid RunId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid? YearEndTaxEmployeeId { get; set; }
    public decimal Amount { get; set; }
    public PayrollAdjustmentDirection Direction { get; set; } = PayrollAdjustmentDirection.Deduction;
    public YearEndTaxAdjustmentStatus Status { get; set; } = YearEndTaxAdjustmentStatus.Recommended;
    public Guid? PayrollAdjustmentId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string SourceCalculationReference { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public Tenant? Tenant { get; set; }
    public YearEndTaxRun? Run { get; set; }
    public Employee? Employee { get; set; }
    public YearEndTaxEmployee? YearEndTaxEmployee { get; set; }
    public PayrollAdjustment? PayrollAdjustment { get; set; }
}

public sealed class YearEndTaxStatement : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid RunId { get; set; }
    public Guid YearEndTaxEmployeeId { get; set; }
    public Guid EmployeeId { get; set; }
    public string StatementReference { get; set; } = string.Empty;
    public DateTime GeneratedAtUtc { get; set; }
    public string SnapshotJson { get; set; } = "{}";
    public Tenant? Tenant { get; set; }
    public YearEndTaxRun? Run { get; set; }
    public YearEndTaxEmployee? YearEndTaxEmployee { get; set; }
    public Employee? Employee { get; set; }
}

public sealed class YearEndTaxHistory : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid RunId { get; set; }
    public YearEndTaxHistoryEvent Event { get; set; }
    public YearEndTaxRunStatus? PreviousStatus { get; set; }
    public YearEndTaxRunStatus? NewStatus { get; set; }
    public Guid? ActorUserId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public string? Message { get; set; }
    public Tenant? Tenant { get; set; }
    public YearEndTaxRun? Run { get; set; }
}
