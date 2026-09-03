namespace HRMS.Application.DTOs.Masters;

public enum MasterImportMode { CreateOnly, CreateOrUpdate }

public sealed record MasterImportRow(int RowNumber, string Code, string Name, string? Description, bool IsActive, string? ParentCode);

public sealed record MasterImportRowResult(int RowNumber, string Code, string Name, string? ParentCode, string Action, IReadOnlyList<string> Errors);

public sealed record MasterImportPreview(string MasterType, MasterImportMode Mode, IReadOnlyList<MasterImportRow> InputRows, IReadOnlyList<MasterImportRowResult> Rows, int TotalRows, int ValidRows, int NewRows, int UpdateRows, int SkippedRows, int ErrorRows);

public sealed record MasterImportConfirmRequest(string MasterType, MasterImportMode Mode, string? FileName, IReadOnlyList<MasterImportRow> Rows);

public sealed record MasterImportResult(Guid BatchId, int TotalRows, int CreatedRows, int UpdatedRows, int SkippedRows, int FailedRows, string Status, DateTime CompletedAtUtc);
