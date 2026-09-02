namespace HRMS.Application.DTOs.Employees;

/// <summary>
/// Read-only DTO for the status of a bulk import batch. Tracks the upload lifecycle from
/// validation through processing to completion or failure.
/// </summary>
public record ImportBatchDto(
    Guid Id,
    string? FileName,
    string ImportedBy,
    int TotalRows,
    int SuccessfulRows,
    int FailedRows,
    int SkippedRows,
    string Status,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    string? Message,
    DateTime CreatedDate);
