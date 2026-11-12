using HRMS.Application.DTOs.Employees;
using HRMS.Domain.Enums;

namespace HRMS.Application.Abstractions;

public interface IEmployeeBulkImportPreflightVerifier
{
    Task<EmployeeBulkImportPreflightReport> VerifyAsync(Guid operationId, CancellationToken cancellationToken = default);
}

/// <summary>Read-only operation projection; no persistence mutation methods are exposed to the verifier.</summary>
public interface IEmployeeImportPreflightReadStore
{
    Task<EmployeeImportPreflightOperationSnapshot?> FindOperationAsync(Guid tenantId, Guid operationId, CancellationToken cancellationToken = default);
    Task<EmployeeImportPreflightRowCounts> CountRowsAsync(Guid tenantId, Guid operationId, CancellationToken cancellationToken = default);
}

public sealed record EmployeeImportPreflightOperationSnapshot(
    Guid TenantId,
    Guid OperationId,
    string WorkbookSha256,
    EmployeeImportOperationState State,
    DateTime? ConfirmedAtUtc,
    int TotalRows,
    int SucceededRows,
    int FailedRows,
    int SkippedRows,
    Guid? ClaimLeaseId,
    DateTime? ClaimLeaseExpiresAtUtc);

public sealed record EmployeeImportPreflightRowCounts(int Pending, int Processing, int Succeeded, int Failed, int Skipped)
{
    public int Total => Pending + Processing + Succeeded + Failed + Skipped;
}

public sealed record EmployeeBulkImportPreflightCheck(string Name, bool Passed, string Category);

/// <summary>Aggregate-only read-only result. Never contains workbook values or row-level employee data.</summary>
public sealed record EmployeeBulkImportPreflightReport(
    Guid OperationId,
    Guid? TenantId,
    string TenantCode,
    string? State,
    DateTime? ConfirmedAtUtc,
    int? ExpectedRows,
    int? PendingRows,
    int? ProcessingRows,
    int? SucceededRows,
    int? FailedRows,
    int? SkippedRows,
    bool? ActiveLease,
    int? ValidatedRows,
    int? InvalidRows,
    IReadOnlyDictionary<string, int> SanitizedErrorCategories,
    IReadOnlyList<EmployeeBulkImportPreflightCheck> Checks)
{
    public bool Passed => Checks.All(check => check.Passed);
}
