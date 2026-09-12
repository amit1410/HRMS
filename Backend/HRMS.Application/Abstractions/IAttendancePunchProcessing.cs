using HRMS.Application.Common;
using HRMS.Domain.Enums;

namespace HRMS.Application.Abstractions;

public sealed record AttendancePunchIngestionRequest(
    Guid EmployeeId,
    DateTime PunchAtUtc,
    PunchDirection Direction,
    PunchSource Source,
    string? ExternalPunchId = null,
    string? DeviceId = null,
    DateOnly? BusinessDate = null,
    string? RawReference = null);

public sealed record AttendancePunchDto(
    Guid Id,
    Guid EmployeeId,
    DateTime PunchAtUtc,
    DateOnly BusinessDate,
    PunchDirection Direction,
    PunchSource Source,
    string? ExternalPunchId,
    string? DeviceId,
    DateTime CapturedAtUtc);

public sealed record AttendancePunchSession(DateTime InAtUtc, DateTime OutAtUtc)
{
    public int WorkedMinutes => Math.Max(0, (int)(OutAtUtc - InAtUtc).TotalMinutes);
}

public sealed record EmployeeAttendanceDayDto(
    Guid Id,
    Guid EmployeeId,
    DateOnly BusinessDate,
    Guid? ShiftId,
    string? ShiftCode,
    DateTime? ScheduledStartUtc,
    DateTime? ScheduledEndUtc,
    int? ExpectedWorkMinutes,
    RosterAssignmentSource RosterAssignmentSource,
    RosterDayType RosterDayType,
    EmployeeAttendanceDayStatus Status,
    DateTime? FirstPunchAtUtc,
    DateTime? LastPunchAtUtc,
    int PunchCount,
    int SessionCount,
    int? WorkedMinutes,
    int? BreakMinutes,
    bool IsLateIn,
    bool IsEarlyOut,
    bool IsGraceApplied,
    bool IsSinglePunch,
    bool HasMissingInPunch,
    bool HasMissingOutPunch,
    bool LeaveConflict,
    bool RequiresMarkOutApproval,
    bool HasInvalidPunchSequence,
    DateTime ProcessedAtUtc,
    string? ProcessingOutcome);

public interface IAttendanceBusinessDateResolver
{
    DateOnly Resolve(DateTime punchAtUtc, DateOnly candidateDate, TimeOnly shiftStart, TimeOnly shiftEnd, bool crossesMidnight, int postShiftMinutes, TimeZoneInfo timeZone);
}

public interface IAttendanceBusinessTimeZoneProvider
{
    TimeZoneInfo GetTimeZone(Guid tenantId);
}

public interface IAttendancePunchIngestionService
{
    Task<Result<AttendancePunchDto>> IngestAsync(AttendancePunchIngestionRequest request, CancellationToken cancellationToken = default);
}

public interface IAttendanceDayProcessor
{
    Task<Result<EmployeeAttendanceDayDto>> ProcessAsync(Guid employeeId, DateOnly businessDate, CancellationToken cancellationToken = default);
}
