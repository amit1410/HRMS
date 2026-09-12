using HRMS.Application.Common;
using HRMS.Domain.Enums;

namespace HRMS.Application.Abstractions;

public sealed class AttendancePeriodQuery : PagedQuery
{
    public int? Year { get; set; }
    public int? Month { get; set; }
    public AttendancePeriodStatus? Status { get; set; }
}

public sealed class AttendanceMonthlySummaryQuery : PagedQuery
{
    public Guid? EmployeeId { get; set; }
    public bool? HasExceptions { get; set; }
}

public sealed class AttendanceExceptionQuery : PagedQuery
{
    public Guid? EmployeeId { get; set; }
    public AttendanceExceptionType? ExceptionType { get; set; }
    public bool? IsBlocking { get; set; }
    public DateOnly? Date { get; set; }
}

public sealed record AttendancePeriodRequest(int Year, int Month);

public sealed record AttendancePeriodDto(Guid Id, int Year, int Month, DateOnly StartDate, DateOnly EndDate, AttendancePeriodStatus Status, int DataVersion, int ConcurrencyVersion, DateTime? ProcessedAtUtc, Guid? LastProcessedByUserId);

public sealed record AttendancePeriodOverviewDto(Guid PeriodId, int EmployeesProcessed, int EmployeesWithExceptions, int TotalExceptions, int BlockingExceptions, DateTime? ProcessedAtUtc, AttendancePeriodStatus Status, int DataVersion);

public sealed record EmployeeAttendanceMonthlySummaryDto(
    Guid Id, Guid AttendancePeriodId, Guid EmployeeId, string? EmployeeCode, string EmployeeName,
    int CalendarDays, int EmploymentDays, int WorkingDays, int PresentDays, int AbsentDays, int OnLeaveDays,
    int OnDutyDays, int HolidayDays, int WeeklyOffDays, int IncompleteDays, int NotProcessedDays,
    int LateInCount, int EarlyOutCount, int GraceAppliedCount, int MissingInCount, int MissingOutCount,
    int RegularizedDays, int ApprovedOnDutyDays, int LeaveConflictCount, int ExceptionCount,
    int ExpectedWorkMinutes, int ActualWorkMinutes, int SourceDataVersion, DateTime ProcessedAtUtc);

public sealed record AttendanceExceptionDto(Guid Key, Guid PeriodId, Guid EmployeeId, DateOnly BusinessDate, AttendanceExceptionType ExceptionType, bool IsBlocking, Guid? SourceId, string Message);

public interface IAttendanceMonthlyProcessor
{
    Task<Result<AttendancePeriodDto>> CreatePeriodAsync(AttendancePeriodRequest request, CancellationToken ct = default);
    Task<Result<PagedResult<AttendancePeriodDto>>> GetPeriodsAsync(AttendancePeriodQuery query, CancellationToken ct = default);
    Task<Result<AttendancePeriodDto>> GetPeriodAsync(Guid periodId, CancellationToken ct = default);
    Task<Result<AttendancePeriodOverviewDto>> ProcessAsync(Guid periodId, CancellationToken ct = default);
    Task<Result<AttendancePeriodOverviewDto>> GetOverviewAsync(Guid periodId, CancellationToken ct = default);
    Task<Result<PagedResult<EmployeeAttendanceMonthlySummaryDto>>> GetSummariesAsync(Guid periodId, AttendanceMonthlySummaryQuery query, CancellationToken ct = default);
    Task<Result<PagedResult<AttendanceExceptionDto>>> GetExceptionsAsync(Guid periodId, AttendanceExceptionQuery query, CancellationToken ct = default);
}
