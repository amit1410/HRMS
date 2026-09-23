using HRMS.Domain.Common;
using HRMS.Domain.Enums;

namespace HRMS.Domain.Entities;

public sealed class PayrollInputBatch : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public string BatchNumber { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? PayrollPeriodId { get; set; }
    public DateOnly EffectiveDate { get; set; }
    public PayrollInputSourceType SourceType { get; set; }
    public PayrollInputBatchStatus Status { get; set; } = PayrollInputBatchStatus.Draft;
    public int TotalRows { get; set; }
    public int ValidRows { get; set; }
    public int InvalidRows { get; set; }
    public int WarningRows { get; set; }
    public decimal TotalAmount { get; set; }
    public string? FileName { get; set; }
    public string? FileHash { get; set; }
    public Guid? TemplateId { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }
    public Guid? SubmittedByUserId { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTime? PostedAtUtc { get; set; }
    public Guid? PostedByUserId { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public Guid? CancelledByUserId { get; set; }
    public string? CancellationReason { get; set; }
    public int ConcurrencyVersion { get; set; } = 1;
    public Tenant? Tenant { get; set; }
    public PayrollPeriod? PayrollPeriod { get; set; }
    public PayrollInputTemplate? Template { get; set; }
    public ICollection<PayrollInputLine> Lines { get; set; } = new List<PayrollInputLine>();
    public ICollection<PayrollInputValidationIssue> Issues { get; set; } = new List<PayrollInputValidationIssue>();
    public ICollection<PayrollInputHistory> History { get; set; } = new List<PayrollInputHistory>();
}

public sealed class PayrollInputLine : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid BatchId { get; set; }
    public int RowNumber { get; set; }
    public Guid? EmployeeId { get; set; }
    public string EmployeeCodeSnapshot { get; set; } = string.Empty;
    public Guid? SalaryComponentId { get; set; }
    public string ComponentCodeSnapshot { get; set; } = string.Empty;
    public PayrollInputType InputType { get; set; }
    public decimal? Amount { get; set; }
    public decimal? Quantity { get; set; }
    public decimal? Rate { get; set; }
    public DateOnly EffectiveDate { get; set; }
    public Guid? PayrollPeriodId { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? Remarks { get; set; }
    public PayrollInputLineStatus Status { get; set; } = PayrollInputLineStatus.Staged;
    public string? ValidationMessage { get; set; }
    public string SourceRowHash { get; set; } = string.Empty;
    public int ConcurrencyVersion { get; set; } = 1;
    public Tenant? Tenant { get; set; }
    public PayrollInputBatch? Batch { get; set; }
    public Employee? Employee { get; set; }
    public SalaryComponent? SalaryComponent { get; set; }
}

public sealed class PayrollInputTemplate : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public PayrollInputType InputType { get; set; }
    public bool Active { get; set; } = true;
    public int HeaderRow { get; set; } = 1;
    public string Delimiter { get; set; } = ",";
    public string DateFormat { get; set; } = "yyyy-MM-dd";
    public int Version { get; set; } = 1;
    public int ConcurrencyVersion { get; set; } = 1;
    public Tenant? Tenant { get; set; }
    public ICollection<PayrollInputTemplateColumn> Columns { get; set; } = new List<PayrollInputTemplateColumn>();
}

public sealed class PayrollInputTemplateColumn : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid TemplateId { get; set; }
    public string SourceColumnName { get; set; } = string.Empty;
    public string TargetField { get; set; } = string.Empty;
    public bool Required { get; set; }
    public int Position { get; set; }
    public string? DefaultValue { get; set; }
    public PayrollInputTemplateTransform TransformType { get; set; }
    public bool Active { get; set; } = true;
    public Tenant? Tenant { get; set; }
    public PayrollInputTemplate? Template { get; set; }
}

public sealed class PayrollInputValidationIssue : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid BatchId { get; set; }
    public Guid? LineId { get; set; }
    public int? RowNumber { get; set; }
    public PayrollInputIssueSeverity Severity { get; set; }
    public string Code { get; set; } = string.Empty;
    public string? FieldName { get; set; }
    public string Message { get; set; } = string.Empty;
    public Tenant? Tenant { get; set; }
    public PayrollInputBatch? Batch { get; set; }
    public PayrollInputLine? Line { get; set; }
}

public sealed class PayrollInputHistory : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid BatchId { get; set; }
    public PayrollInputHistoryEvent Event { get; set; }
    public PayrollInputBatchStatus? PreviousStatus { get; set; }
    public PayrollInputBatchStatus? NewStatus { get; set; }
    public Guid? ActorUserId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public string? Message { get; set; }
    public Tenant? Tenant { get; set; }
    public PayrollInputBatch? Batch { get; set; }
}
