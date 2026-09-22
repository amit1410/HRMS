using HRMS.Domain.Common;
using HRMS.Domain.Enums;

namespace HRMS.Domain.Entities;

public sealed class PayrollVarianceControl : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public PayrollControlScope Scope { get; set; }
    public PayrollControlMetric Metric { get; set; }
    public decimal? AbsoluteThreshold { get; set; }
    public decimal? PercentageThreshold { get; set; }
    public PayrollFindingSeverity Severity { get; set; } = PayrollFindingSeverity.Warning;
    public PayrollControlAction Action { get; set; } = PayrollControlAction.Informational;
    public PayrollRunType? AppliesToRunType { get; set; }
    public bool IsActive { get; set; } = true;
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public int ConcurrencyVersion { get; set; } = 1;
    public Tenant? Tenant { get; set; }
}

public sealed class PayrollAnalyticsSnapshot : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid PayrollRunId { get; set; }
    public PayrollAnalyticsSnapshotType SnapshotType { get; set; }
    public DateTime GeneratedAtUtc { get; set; }
    public Guid? GeneratedByUserId { get; set; }
    public int EmployeeCount { get; set; }
    public decimal GrossTotal { get; set; }
    public decimal EarningsTotal { get; set; }
    public decimal DeductionTotal { get; set; }
    public decimal EmployerContributionTotal { get; set; }
    public decimal TaxTotal { get; set; }
    public decimal NetPayTotal { get; set; }
    public decimal ReimbursementTotal { get; set; }
    public decimal LoanRecoveryTotal { get; set; }
    public decimal VariablePayTotal { get; set; }
    public decimal AdjustmentTotal { get; set; }
    public decimal? FinalSettlementTotal { get; set; }
    public string CurrencyCode { get; set; } = "INR";
    public int DataVersion { get; set; } = 1;
    public Tenant? Tenant { get; set; }
    public PayrollRun? PayrollRun { get; set; }
}

public sealed class PayrollReconciliation : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid PayrollRunId { get; set; }
    public PayrollReconciliationType ReconciliationType { get; set; }
    public int Version { get; set; } = 1;
    public PayrollReconciliationStatus Status { get; set; } = PayrollReconciliationStatus.Generated;
    public DateTime GeneratedAtUtc { get; set; }
    public Guid? GeneratedByUserId { get; set; }
    public int TotalChecks { get; set; }
    public int PassedChecks { get; set; }
    public int WarningChecks { get; set; }
    public int FailedChecks { get; set; }
    public int CriticalChecks { get; set; }
    public DateTime? AcknowledgedAtUtc { get; set; }
    public Guid? AcknowledgedByUserId { get; set; }
    public int ConcurrencyVersion { get; set; } = 1;
    public Tenant? Tenant { get; set; }
    public PayrollRun? PayrollRun { get; set; }
    public ICollection<PayrollReconciliationFinding> Findings { get; set; } = new List<PayrollReconciliationFinding>();
}

public sealed class PayrollReconciliationFinding : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid PayrollReconciliationId { get; set; }
    public string ControlCode { get; set; } = string.Empty;
    public PayrollControlScope Scope { get; set; }
    public PayrollFindingSeverity Severity { get; set; }
    public PayrollControlAction Action { get; set; }
    public Guid? EmployeeId { get; set; }
    public Guid? PayrollResultId { get; set; }
    public Guid? SalaryComponentId { get; set; }
    public string? SourceType { get; set; }
    public Guid? SourceId { get; set; }
    public PayrollControlMetric Metric { get; set; }
    public decimal? ExpectedValue { get; set; }
    public decimal? ActualValue { get; set; }
    public decimal? Difference { get; set; }
    public decimal? VariancePercent { get; set; }
    public string Message { get; set; } = string.Empty;
    public PayrollFindingStatus Status { get; set; } = PayrollFindingStatus.Open;
    public DateTime GeneratedAtUtc { get; set; }
    public DateTime? AcknowledgedAtUtc { get; set; }
    public Guid? AcknowledgedByUserId { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
    public Guid? ResolvedByUserId { get; set; }
    public string? ResolutionNote { get; set; }
    public string? ResolutionReference { get; set; }
    public Tenant? Tenant { get; set; }
    public PayrollReconciliation? Reconciliation { get; set; }
}

public sealed class PayrollAnomalyFlag : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid PayrollRunId { get; set; }
    public Guid? EmployeeId { get; set; }
    public Guid? PayrollResultId { get; set; }
    public Guid? ControlId { get; set; }
    public PayrollAnomalyType AnomalyType { get; set; }
    public PayrollFindingSeverity Severity { get; set; }
    public PayrollControlMetric Metric { get; set; }
    public decimal? CurrentValue { get; set; }
    public decimal? ComparisonValue { get; set; }
    public decimal? Difference { get; set; }
    public decimal? VariancePercent { get; set; }
    public string Message { get; set; } = string.Empty;
    public PayrollFindingStatus Status { get; set; } = PayrollFindingStatus.Open;
    public DateTime CreatedAtUtc { get; set; }
    public bool CreatedBySystem { get; set; } = true;
    public DateTime? AcknowledgedAtUtc { get; set; }
    public Guid? AcknowledgedByUserId { get; set; }
    public string? ResolutionNote { get; set; }
    public Tenant? Tenant { get; set; }
}
