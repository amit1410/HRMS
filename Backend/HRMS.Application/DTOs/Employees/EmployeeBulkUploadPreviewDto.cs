namespace HRMS.Application.DTOs.Employees;

public sealed record EmployeeBulkUploadPreviewDto(int TotalRows, int ValidRows, int InvalidRows, int WarningRows, IReadOnlyList<EmployeeBulkUploadPreviewRow> Rows);
public sealed record EmployeeBulkUploadPreviewRow(int RowNumber, string Status, IReadOnlyList<EmployeeBulkUploadIssue> Issues);
public sealed record EmployeeBulkUploadIssue(string FieldName, string ColumnName, int ColumnNumber, string ErrorCode, string Message, string Guidance);
