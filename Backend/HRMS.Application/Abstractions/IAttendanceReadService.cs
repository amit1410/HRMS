using HRMS.Application.Common;
using HRMS.Domain.Enums;

namespace HRMS.Application.Abstractions;

public sealed class AttendanceCalendarQuery
{
    public int Year { get; set; }
    public int Month { get; set; }
}

public sealed class ManagerAttendanceQuery : PagedQuery
{
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    public Guid? EmployeeId { get; set; }
    public EmployeeAttendanceDayStatus? Status { get; set; }
    public RosterDayType? DayType { get; set; }
    public Guid? ShiftId { get; set; }
    public string? Variance { get; set; }
}

public sealed record AttendanceCalendarDayDto(
    DateOnly Date,
    DayOfWeek DayOfWeek,
    RosterDayType DayType,
    EmployeeAttendanceDayStatus AttendanceStatus,
    Guid? ShiftId,
    string? ShiftCode,
    string? ShiftName,
    DateTime? ScheduledStartUtc,
    DateTime? ScheduledEndUtc,
    DateTime? FirstPunchAtUtc,
    DateTime? LastPunchAtUtc,
    int? WorkedMinutes,
    int? ExpectedWorkMinutes,
    RosterAssignmentSource AssignmentSource,
    bool IsOverride,
    RosterCalendarDayType CalendarSource,
    bool IsLateIn,
    bool IsEarlyOut,
    bool IsGraceApplied,
    bool IsSinglePunch,
    bool HasMissingInPunch,
    bool HasMissingOutPunch,
    bool HasInvalidPunchSequence,
    bool LeaveConflict,
    bool RequiresMarkOutApproval,
    int PunchCount,
    int SessionCount,
    bool IsProcessed,
    string? ProcessingMessage);

public sealed record AttendancePunchSessionDto(DateTime InAtUtc, DateTime OutAtUtc, int WorkedMinutes);

public sealed record AttendanceDayDetailDto(
    AttendanceCalendarDayDto Day,
    IReadOnlyList<AttendancePunchDto> Punches,
    IReadOnlyList<AttendancePunchSessionDto> Sessions);

public sealed record ManagerAttendanceRowDto(
    AttendanceCalendarDayDto Day,
    Guid EmployeeId,
    string EmployeeCode,
    string EmployeeName,
    string? Department);

public sealed record AttendanceSummaryDto(
    int Present,
    int Absent,
    int OnLeave,
    int Holiday,
    int WeeklyOff,
    int Incomplete,
    int NotProcessed,
    int Late,
    int EarlyOut,
    int LeaveConflict);

public sealed record ManagerAttendanceResult(
    PagedResult<ManagerAttendanceRowDto> Rows,
    AttendanceSummaryDto Summary);

public interface IAttendanceReadService
{
    Task<Result<IReadOnlyList<AttendanceCalendarDayDto>>> GetMyCalendarAsync(AttendanceCalendarQuery query, CancellationToken ct = default);
    Task<Result<AttendanceDayDetailDto>> GetMyDayAsync(DateOnly date, CancellationToken ct = default);
    Task<Result<ManagerAttendanceResult>> GetManagerTeamAsync(ManagerAttendanceQuery query, CancellationToken ct = default);
    Task<Result<AttendanceDayDetailDto>> GetManagerDayAsync(Guid employeeId, DateOnly date, CancellationToken ct = default);
}
