using HRMS.Domain.Common;
using HRMS.Domain.Enums;

namespace HRMS.Domain.Entities;

public enum LeaveBalanceImportBatchStatus
{
    Validated = 0,
    Invalid = 1,
    Committed = 2,
    Failed = 3
}

public enum LeaveBalanceImportRowStatus
{
    Valid = 0,
    Invalid = 1,
    Imported = 2
}

public sealed class LeaveBalanceImportBatch : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public LeaveBalanceImportBatchStatus Status { get; set; }
    public int TotalRows { get; set; }
    public int ValidRows { get; set; }
    public int InvalidRows { get; set; }
    public int ImportedRows { get; set; }
    public Guid UploadedByUserId { get; set; }
    public DateTime UploadedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? FailureReason { get; set; }
    public byte[]? RowVersion { get; set; }
    public Tenant? Tenant { get; set; }
    public User? UploadedByUser { get; set; }
    public ICollection<LeaveBalanceImportRow> Rows { get; set; } = new List<LeaveBalanceImportRow>();
}

public sealed class LeaveBalanceImportRow : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid BatchId { get; set; }
    public int RowNumber { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string LeaveTypeCode { get; set; } = string.Empty;
    public string LeavePeriod { get; set; } = string.Empty;
    public string OpeningBalanceText { get; set; } = string.Empty;
    public decimal? OpeningBalance { get; set; }
    public string EffectiveDateText { get; set; } = string.Empty;
    public DateOnly? EffectiveDate { get; set; }
    public string? Remarks { get; set; }
    public LeaveBalanceImportRowStatus Status { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public Guid? EmployeeId { get; set; }
    public Guid? LeaveTypeId { get; set; }
    public Guid? LeavePeriodId { get; set; }
    public string? IdempotencyKey { get; set; }
    public Tenant? Tenant { get; set; }
    public LeaveBalanceImportBatch? Batch { get; set; }
    public Employee? Employee { get; set; }
    public LeaveType? LeaveType { get; set; }
    public LeavePeriod? LeavePeriodEntity { get; set; }
}
