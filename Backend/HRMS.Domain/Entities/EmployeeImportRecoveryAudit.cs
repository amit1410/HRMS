using HRMS.Domain.Common;

namespace HRMS.Domain.Entities;

/// <summary>Append-only, sanitized record of an operator's employee-import recovery attempt.</summary>
public sealed class EmployeeImportRecoveryAudit : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid OperationId { get; set; }
    public DateTime RecoveryAtUtc { get; set; }
    public string Operator { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public int BeforeTotalRows { get; set; }
    public int BeforeSucceededRows { get; set; }
    public int BeforeFailedRows { get; set; }
    public int BeforeSkippedRows { get; set; }
    public int BeforePendingRows { get; set; }
    public int AfterTotalRows { get; set; }
    public int AfterSucceededRows { get; set; }
    public int AfterFailedRows { get; set; }
    public int AfterSkippedRows { get; set; }
    public int AfterPendingRows { get; set; }
    public string? SanitizedErrorCode { get; set; }
}
