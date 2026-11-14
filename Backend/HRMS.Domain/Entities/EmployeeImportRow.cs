using HRMS.Domain.Common;
using HRMS.Domain.Enums;

namespace HRMS.Domain.Entities;

/// <summary>Sanitized durable outcome for one workbook row; never stores the source row payload.</summary>
public sealed class EmployeeImportRow : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid OperationId { get; set; }
    public int ExcelRowNumber { get; set; }
    public EmployeeImportRowStatus Status { get; private set; } = EmployeeImportRowStatus.Pending;
    public Guid? EmployeeId { get; private set; }
    public string? EmployeeCode { get; private set; }
    public string? ErrorCode { get; private set; }
    public string? SanitizedErrorMessage { get; private set; }
    public DateTime? ProcessingStartedAtUtc { get; private set; }
    public DateTime? ProcessingCompletedAtUtc { get; private set; }
    public EmployeeImportOperation? Operation { get; set; }

    public bool Start(DateTime nowUtc)
    {
        if (Status != EmployeeImportRowStatus.Pending) return false;
        Status = EmployeeImportRowStatus.Processing;
        ProcessingStartedAtUtc = nowUtc;
        return true;
    }

    public bool Complete(Guid employeeId, string? employeeCode, DateTime nowUtc)
    {
        if (Status != EmployeeImportRowStatus.Processing || employeeId == Guid.Empty) return false;
        Status = EmployeeImportRowStatus.Succeeded;
        EmployeeId = employeeId;
        EmployeeCode = employeeCode;
        ProcessingCompletedAtUtc = nowUtc;
        return true;
    }

    public bool Fail(string errorCode, string sanitizedMessage, DateTime nowUtc, string? employeeCode = null)
    {
        if (Status != EmployeeImportRowStatus.Processing) return false;
        Status = EmployeeImportRowStatus.Failed;
        EmployeeCode = string.IsNullOrWhiteSpace(employeeCode) ? null : employeeCode;
        ErrorCode = errorCode;
        SanitizedErrorMessage = sanitizedMessage;
        ProcessingCompletedAtUtc = nowUtc;
        return true;
    }

    public bool Skip(string errorCode, string sanitizedMessage, DateTime nowUtc)
    {
        if (Status is not (EmployeeImportRowStatus.Pending or EmployeeImportRowStatus.Processing)) return false;
        Status = EmployeeImportRowStatus.Skipped;
        ErrorCode = errorCode;
        SanitizedErrorMessage = sanitizedMessage;
        ProcessingCompletedAtUtc = nowUtc;
        return true;
    }

    /// <summary>
    /// Resets a stale in-flight row after a restart. A successful employee and this row's success are
    /// written in the same transaction, so a Processing row with no EmployeeId proves that unit did not commit.
    /// The caller must still re-upload and revalidate the workbook before retrying.
    /// </summary>
    public bool ResetAbandonedProcessing()
    {
        if (Status != EmployeeImportRowStatus.Processing || EmployeeId.HasValue) return false;
        Status = EmployeeImportRowStatus.Pending;
        ProcessingStartedAtUtc = null;
        ProcessingCompletedAtUtc = null;
        ErrorCode = null;
        SanitizedErrorMessage = null;
        return true;
    }

    public bool RequeueForRecovery()
    {
        if (EmployeeId.HasValue || Status is not (EmployeeImportRowStatus.Failed or EmployeeImportRowStatus.Skipped)) return false;
        Status = EmployeeImportRowStatus.Pending;
        ErrorCode = null;
        SanitizedErrorMessage = null;
        ProcessingStartedAtUtc = null;
        ProcessingCompletedAtUtc = null;
        return true;
    }
}
