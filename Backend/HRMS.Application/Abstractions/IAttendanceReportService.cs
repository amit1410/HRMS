using HRMS.Application.Common;
using HRMS.Domain.Enums;

namespace HRMS.Application.Abstractions;

public sealed class AttendanceDailyReportQuery : PagedQuery
{
    public DateOnly? FromDate { get; set; }
    public DateOnly? ToDate { get; set; }
    public Guid? EmployeeId { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid? WorkLocationId { get; set; }
    public EmployeeAttendanceDayStatus? Status { get; set; }
}

public sealed class AttendanceMonthlyReportQuery : PagedQuery
{
    public Guid? PeriodId { get; set; }
    public int? Year { get; set; }
    public int? Month { get; set; }
    public Guid? EmployeeId { get; set; }
}

public sealed class AttendanceExceptionReportQuery : PagedQuery
{
    public Guid PeriodId { get; set; }
    public Guid? EmployeeId { get; set; }
    public AttendanceExceptionType? ExceptionType { get; set; }
    public bool? IsBlocking { get; set; }
    public DateOnly? FromDate { get; set; }
    public DateOnly? ToDate { get; set; }
}

public sealed record AttendanceDailyReportRow(Guid EmployeeId, string? EmployeeCode, string EmployeeName, DateOnly BusinessDate, EmployeeAttendanceDayStatus Status, DateTime? InTimeUtc, DateTime? OutTimeUtc, int? ActualWorkMinutes, int? ExpectedWorkMinutes, bool IsLateIn, bool IsEarlyOut, string? ShiftCode, string? Department, string? WorkLocation);

public sealed record AttendanceMonthlyReportRow(Guid PeriodId, int Year, int Month, AttendancePeriodStatus PeriodStatus, string? EmployeeCode, string EmployeeName, int WorkingDays, int PresentDays, int AbsentDays, int OnLeaveDays, int OnDutyDays, int IncompleteDays, int NotProcessedDays, int ExpectedWorkMinutes, int ActualWorkMinutes, int ExceptionCount, int SourceDataVersion);

public sealed record AttendanceReportExport(string FileName, string ContentType, byte[] Content, int RowCount);

public interface IAttendanceReportService
{
    Task<Result<PagedResult<AttendanceDailyReportRow>>> GetDailyAsync(AttendanceDailyReportQuery query, CancellationToken ct = default);
    Task<Result<PagedResult<AttendanceMonthlyReportRow>>> GetMonthlyAsync(AttendanceMonthlyReportQuery query, CancellationToken ct = default);
    Task<Result<PagedResult<AttendanceExceptionDto>>> GetExceptionsAsync(AttendanceExceptionReportQuery query, CancellationToken ct = default);
    Task<Result<AttendanceReportExport>> ExportDailyAsync(AttendanceDailyReportQuery query, CancellationToken ct = default);
    Task<Result<AttendanceReportExport>> ExportMonthlyAsync(AttendanceMonthlyReportQuery query, CancellationToken ct = default);
    Task<Result<AttendanceReportExport>> ExportExceptionsAsync(AttendanceExceptionReportQuery query, CancellationToken ct = default);
}
