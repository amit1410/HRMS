namespace HRMS.Application.DTOs.Employees;

public sealed record EmployeeImportRecoveryReport(
    bool Passed,
    bool Applied,
    Guid OperationId,
    Guid TenantId,
    string State,
    int TotalRows,
    int SucceededRows,
    int FailedRows,
    int SkippedRows,
    int PendingRows,
    int RowsToRequeue,
    DateTime? LeaseExpiresAtUtc,
    IReadOnlyList<string> Blockers,
    string Outcome)
{
    public IReadOnlyList<EmployeeImportValidationDiagnostic> ValidationDiagnostics { get; init; } = [];
    public bool? RecoveryAuditMigrationApplied { get; init; }
    public string? MigrationStatusErrorCategory { get; init; }
}

public sealed record EmployeeImportValidationDiagnostic(
    string Stage,
    string ErrorCode,
    string ColumnName,
    int? AffectedRows,
    IReadOnlyList<int> RowNumbers,
    string? OperationCategory = null,
    string? ResourceType = null,
    string? ExceptionType = null,
    string? SafeHResult = null,
    bool? ResourceInsideApprovedWorkspace = null);
