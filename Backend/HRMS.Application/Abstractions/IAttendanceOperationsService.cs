using HRMS.Application.Common;
using HRMS.Application.DTOs.Employees;
using HRMS.Domain.Enums;

namespace HRMS.Application.Abstractions;

public sealed class AttendanceOperationsExceptionQuery : PagedQuery
{
    public DateOnly? FromDate { get; set; }
    public DateOnly? ToDate { get; set; }
    public Guid? EmployeeId { get; set; }
    public AttendanceExceptionType? ExceptionType { get; set; }
    public EmployeeAttendanceDayStatus? AttendanceStatus { get; set; }
    public bool? IsResolved { get; set; }
}

public sealed record AttendanceOperationalExceptionDto(
    Guid Id,
    Guid EmployeeId,
    string? EmployeeCode,
    string EmployeeName,
    DateOnly BusinessDate,
    string? ShiftCode,
    AttendanceExceptionType ExceptionType,
    EmployeeAttendanceDayStatus AttendanceStatus,
    DateTime? ScheduledStartUtc,
    DateTime? ScheduledEndUtc,
    DateTime? FirstPunchAtUtc,
    DateTime? LastPunchAtUtc,
    int? WorkedMinutes,
    int? ExpectedWorkMinutes,
    int LateMinutes,
    int EarlyDepartureMinutes,
    bool IsBlocking,
    bool IsResolved,
    int AgeDays,
    int AttendanceVersion,
    Guid? RelatedRequestId,
    string Message);

public sealed record AttendanceOperationsDashboardDto(
    DateOnly FromDate,
    DateOnly ToDate,
    int Employees,
    int ProcessedDays,
    int PresentDays,
    int AbsentDays,
    int LeaveDays,
    int OnDutyDays,
    int WeeklyOffDays,
    int HolidayDays,
    int ExceptionDays,
    int LateDays,
    int EarlyDepartureDays,
    int PendingRegularizations,
    int PendingOnDuty,
    int OpenPeriods,
    int FinalizedPeriods,
    int ActiveAbsentExceptions,
    int MissedPunchExceptions,
    int PendingCorrections,
    int MissingInPunchExceptions,
    int MissingOutPunchExceptions);

public sealed record AttendanceBulkActionItem(Guid RequestId, bool IsOnDuty, bool Approve, int ExpectedVersion, string? Comments);
public sealed record AttendanceBulkActionResult(Guid RequestId, bool IsOnDuty, bool Success, string? FailureCode, string Message, int? CurrentVersion);
public sealed record AttendanceBulkActionResponse(IReadOnlyList<AttendanceBulkActionResult> Items, int Succeeded, int Failed);
public sealed record ManualAttendanceRequest(Guid EmployeeId, DateOnly BusinessDate, AttendanceRegularizationType CorrectionType, DateTime? ProposedInAtUtc, DateTime? ProposedOutAtUtc, string Reason, string? Comments, int ExpectedAttendanceVersion);
public sealed record AttendanceBulkCorrectionItem(Guid EmployeeId, DateOnly BusinessDate, AttendanceRegularizationType CorrectionType, DateTime? ProposedInAtUtc, DateTime? ProposedOutAtUtc, string Reason, int ExpectedAttendanceVersion);
public sealed record AttendanceBulkCorrectionResult(Guid EmployeeId, DateOnly BusinessDate, bool Success, string? FailureCode, string Message, Guid? RequestId, int? CurrentVersion);
public sealed record AttendanceBulkCorrectionResponse(IReadOnlyList<AttendanceBulkCorrectionResult> Items, int Succeeded, int Failed);
public sealed record AttendanceExceptionResolutionRequest(Guid AttendanceDayId, AttendanceExceptionType ExceptionType, string Action, string Reason, int ExpectedAttendanceVersion);
public sealed record AttendanceOperationsHistoryItem(Guid Id, Guid EmployeeId, Guid? AttendanceDayId, DateOnly? BusinessDate, string Action, Guid ActorUserId, DateTime OccurredAtUtc, string? Reason, string? OldValue, string? NewValue, Guid? ReferenceId, int? AttendanceVersion, int? OldAttendanceVersion = null, int? NewAttendanceVersion = null);
public sealed class AttendanceOperationsHistoryQuery : PagedQuery { }

public sealed record AttendanceOperationsExport(string FileName, string ContentType, byte[] Content, int RowCount);

public interface IAttendanceOperationsService
{
    Task<Result<PagedResult<AttendanceOperationalExceptionDto>>> GetMyExceptionsAsync(AttendanceOperationsExceptionQuery query, CancellationToken ct = default);
    Task<Result<PagedResult<AttendanceOperationalExceptionDto>>> GetOperationalExceptionsAsync(AttendanceOperationsExceptionQuery query, CancellationToken ct = default);
    Task<Result<AttendanceOperationsDashboardDto>> GetDashboardAsync(DateOnly fromDate, DateOnly toDate, CancellationToken ct = default);
    Task<Result<RegularizationDto>> SubmitManualAttendanceAsync(ManualAttendanceRequest request, CancellationToken ct = default);
    Task<Result<AttendanceBulkCorrectionResponse>> ApplyBulkCorrectionsAsync(IReadOnlyList<AttendanceBulkCorrectionItem> items, CancellationToken ct = default);
    Task<Result<EmployeeAuditLogDto>> ResolveExceptionAsync(AttendanceExceptionResolutionRequest request, CancellationToken ct = default);
    Task<Result<PagedResult<AttendanceOperationsHistoryItem>>> GetHistoryAsync(Guid employeeId, DateOnly? fromDate, DateOnly? toDate, PagedQuery query, CancellationToken ct = default);
    Task<Result<AttendanceBulkActionResponse>> ApplyBulkActionAsync(IReadOnlyList<AttendanceBulkActionItem> items, CancellationToken ct = default);
    Task<Result<AttendanceOperationsExport>> ExportExceptionsAsync(AttendanceOperationsExceptionQuery query, CancellationToken ct = default);
}
