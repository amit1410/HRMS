namespace HRMS.Application.DTOs.Banks;

/// <summary>How an import treats rows whose bank code already exists.</summary>
public enum BankImportMode
{
    /// <summary>Only create new banks; an existing code is reported as an error and never overwritten.</summary>
    CreateOnly,

    /// <summary>Create new banks and update existing ones by code.</summary>
    CreateOrUpdate
}

/// <summary>File formats supported for the Bank master template, import and export.</summary>
public enum BankFileFormat
{
    Csv,
    Xlsx
}

/// <summary>One parsed row from an uploaded file, before validation. Raw text is preserved for feedback.</summary>
public sealed record BankImportRow(
    int RowNumber,
    string BankCode,
    string BankName,
    string? ShortName,
    string? IfscPrefix,
    string? BankType,
    string? Country,
    bool Active,
    DateOnly? EffectiveFrom,
    string? Remarks);

/// <summary>The validation outcome for a single row.</summary>
public sealed record BankImportRowResult(
    int RowNumber,
    string BankCode,
    string BankName,
    string Action,
    IReadOnlyList<string> Errors);

/// <summary>The result of validating an uploaded file: the parsed rows, their per-row outcomes and totals.</summary>
public sealed record BankImportPreview(
    BankImportMode Mode,
    IReadOnlyList<BankImportRow> InputRows,
    IReadOnlyList<BankImportRowResult> Rows,
    int TotalRows,
    int ValidRows,
    int NewRows,
    int UpdateRows,
    int SkippedRows,
    int ErrorRows);

/// <summary>The confirmed set of rows to import, echoed back from a validated preview.</summary>
public sealed record BankImportConfirmRequest(
    BankImportMode Mode,
    string? FileName,
    IReadOnlyList<BankImportRow> Rows);

/// <summary>The summary of a completed import.</summary>
public sealed record BankImportResult(
    Guid BatchId,
    int TotalRows,
    int CreatedRows,
    int UpdatedRows,
    int SkippedRows,
    int FailedRows,
    string Status,
    DateTime CompletedAtUtc);

/// <summary>A row in the Bank master import history.</summary>
public sealed record BankImportHistoryDto(
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
