using HRMS.Application.Common;
using HRMS.Domain.Enums;

namespace HRMS.Application.DTOs.Payroll;

public sealed class PayrollInputTemplateRequest { public string Code { get; set; } = string.Empty; public string Name { get; set; } = string.Empty; public string? Description { get; set; } public PayrollInputType InputType { get; set; } public bool Active { get; set; } = true; public string DateFormat { get; set; } = "yyyy-MM-dd"; public List<PayrollInputTemplateColumnRequest> Columns { get; set; } = []; }
public sealed class PayrollInputTemplateColumnRequest { public string SourceColumnName { get; set; } = string.Empty; public string TargetField { get; set; } = string.Empty; public bool Required { get; set; } public int Position { get; set; } public string? DefaultValue { get; set; } public PayrollInputTemplateTransform TransformType { get; set; } }
public sealed record PayrollInputTemplateDto(Guid Id, string Code, string Name, string? Description, PayrollInputType InputType, bool Active, int Version, IReadOnlyList<PayrollInputTemplateColumnDto> Columns);
public sealed record PayrollInputTemplateColumnDto(Guid Id, string SourceColumnName, string TargetField, bool Required, int Position, string? DefaultValue, PayrollInputTemplateTransform TransformType);
public sealed class PayrollInputBatchRequest { public string Name { get; set; } = string.Empty; public string? Description { get; set; } public Guid? PayrollPeriodId { get; set; } public DateOnly EffectiveDate { get; set; } public PayrollInputSourceType SourceType { get; set; } = PayrollInputSourceType.Manual; public Guid? TemplateId { get; set; } public List<PayrollInputLineRequest> Lines { get; set; } = []; }
public class PayrollInputLineRequest { public string EmployeeCode { get; set; } = string.Empty; public string ComponentCode { get; set; } = string.Empty; public PayrollInputType InputType { get; set; } public decimal? Amount { get; set; } public decimal? Quantity { get; set; } public decimal? Rate { get; set; } public DateOnly EffectiveDate { get; set; } public Guid? PayrollPeriodId { get; set; } public string? ReferenceNumber { get; set; } public string? Remarks { get; set; } }
public sealed class PayrollInputLineUpdateRequest : PayrollInputLineRequest { }
public sealed record PayrollInputBatchDto(Guid Id, string BatchNumber, string Name, Guid? PayrollPeriodId, DateOnly EffectiveDate, PayrollInputSourceType SourceType, PayrollInputBatchStatus Status, int TotalRows, int ValidRows, int InvalidRows, int WarningRows, decimal TotalAmount, string? FileName);
public sealed record PayrollInputLineDto(Guid Id, int RowNumber, string EmployeeCode, string ComponentCode, PayrollInputType InputType, decimal? Amount, DateOnly EffectiveDate, PayrollInputLineStatus Status, string? ValidationMessage);
public sealed record PayrollInputIssueDto(Guid Id, Guid? LineId, int? RowNumber, PayrollInputIssueSeverity Severity, string Code, string? FieldName, string Message);
public sealed record PayrollInputPreviewDto(PayrollInputBatchDto Batch, int EmployeeCount, int ComponentCount, decimal Earnings, decimal Deductions, decimal NetInputImpact);
public sealed record PayrollInputHistoryDto(PayrollInputHistoryEvent Event, PayrollInputBatchStatus? PreviousStatus, PayrollInputBatchStatus? NewStatus, DateTime OccurredAtUtc, Guid? ActorUserId, string? Message);
public sealed class PayrollInputBatchQuery : PagedQuery { public PayrollInputBatchStatus? Status { get; set; } public Guid? PayrollPeriodId { get; set; } }
