namespace HRMS.Application.DTOs.Employees;

public sealed record EmployeeBulkImportPreparationDto(Guid OperationId, string ConfirmationToken, int TotalRows, string Summary);
public sealed record EmployeeBulkImportRowResultDto(int ExcelRowNumber, string Status, string? EmployeeCode, string? ErrorCode, string? Message);
public sealed record EmployeeBulkImportExecutionDto(Guid OperationId, int TotalRows, int SucceededRows, int FailedRows, int SkippedRows, string State, IReadOnlyList<EmployeeBulkImportRowResultDto> Rows);
public sealed record EmployeeBulkImportAcceptedDto(Guid OperationId, string State, string StatusUrl);
public sealed record EmployeeBulkImportStatusDto(
    Guid OperationId,
    string State,
    int TotalRows,
    int SucceededRows,
    int FailedRows,
    int SkippedRows,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    DateTime LastUpdatedAtUtc,
    string? Summary,
    IReadOnlyList<EmployeeBulkImportRowResultDto> Rows);

public sealed record EmployeeBulkImportHistoryItemDto(
    Guid OperationId,
    string State,
    int TotalRows,
    int SucceededRows,
    int FailedRows,
    int SkippedRows,
    DateTime PreparedAtUtc,
    DateTime? CompletedAtUtc,
    string? Summary);

public sealed record EmployeeBulkImportHistoryDto(
    IReadOnlyList<EmployeeBulkImportHistoryItemDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);
