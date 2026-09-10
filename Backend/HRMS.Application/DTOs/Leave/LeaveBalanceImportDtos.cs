using HRMS.Domain.Entities;

namespace HRMS.Application.DTOs.Leave;

public sealed record LeaveBalanceImportBatchDto(Guid Id, string FileName, LeaveBalanceImportBatchStatus Status, int TotalRows, int ValidRows, int InvalidRows, int ImportedRows, Guid UploadedByUserId, DateTime UploadedAtUtc, DateTime? CompletedAtUtc, string? FailureReason);
public sealed record LeaveBalanceImportRowDto(int RowNumber, string EmployeeCode, string LeaveTypeCode, string LeavePeriod, string OpeningBalance, string EffectiveDate, string? Remarks, LeaveBalanceImportRowStatus Status, string? ErrorCode, string? ErrorMessage);
