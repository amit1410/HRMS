using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

public sealed class AttendanceDayProcessor(
    IHrmsDbContext db,
    ITenantContext tenant,
    IAttendanceFoundationService roster,
    TimeProvider? timeProvider = null) : IAttendanceDayProcessor
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    public async Task<Result<EmployeeAttendanceDayDto>> ProcessAsync(Guid employeeId, DateOnly businessDate, CancellationToken cancellationToken = default)
    {
        if (tenant.TenantId is not Guid tenantId || tenantId == Guid.Empty) return Result<EmployeeAttendanceDayDto>.Unauthorized("No authenticated tenant.");
        if (!await db.Employees.AnyAsync(x => x.TenantId == tenantId && x.Id == employeeId, cancellationToken)) return Result<EmployeeAttendanceDayDto>.NotFound("Employee was not found in this tenant.");
        var resolution = await roster.ResolveAsync(employeeId, businessDate, cancellationToken);
        if (!resolution.Succeeded) return Result<EmployeeAttendanceDayDto>.Failure(resolution.Status, resolution.Message, resolution.Errors);
        var value = resolution.Value!;
        var shift = value.ShiftId is Guid shiftId ? await db.Shifts.AsNoTracking().Include(x => x.Breaks).SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == shiftId, cancellationToken) : null;
        var punches = await db.AttendancePunches.AsNoTracking().Where(x => x.TenantId == tenantId && x.EmployeeId == employeeId && x.BusinessDate == businessDate).OrderBy(x => x.PunchAtUtc).ThenBy(x => x.Id).ToListAsync(cancellationToken);
        var adjustment = await db.AttendanceAdjustments.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId && x.EmployeeId == employeeId && x.BusinessDate == businessDate, cancellationToken);
        var onDuty = await db.AttendanceOnDutyRequests.AsNoTracking().AnyAsync(x => x.TenantId == tenantId && x.EmployeeId == employeeId && x.Status == AttendanceRequestStatus.Approved && x.StartDate <= businessDate && businessDate <= x.EndDate, cancellationToken);
        var leave = await db.LeaveRequestDays.AsNoTracking().AnyAsync(x => x.TenantId == tenantId && x.Date == businessDate && x.LeaveRequest != null && x.LeaveRequest.EmployeeId == employeeId && x.LeaveRequest.Status == LeaveRequestStatus.Approved, cancellationToken);
        var now = _clock.GetUtcNow().UtcDateTime;
        var existing = await db.EmployeeAttendanceDays.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.EmployeeId == employeeId && x.BusinessDate == businessDate, cancellationToken);
        var day = existing ?? new EmployeeAttendanceDay { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId, BusinessDate = businessDate };
        if (existing is null) db.EmployeeAttendanceDays.Add(day);

        day.ShiftId = shift?.Id; day.ShiftCode = shift?.ShiftCode; day.ExpectedWorkMinutes = shift?.FullDayWorkMinutes > 0 ? shift.FullDayWorkMinutes : shift?.PlannedDurationMinutes; day.RosterAssignmentSource = value.Source; day.RosterDayType = value.PatternDayType == ShiftPatternDayType.WeeklyOff ? RosterDayType.WeeklyOff : shift is null && value.Message.Contains("Holiday", StringComparison.OrdinalIgnoreCase) ? RosterDayType.Holiday : shift is null && value.Message.Contains("WeeklyOff", StringComparison.OrdinalIgnoreCase) ? RosterDayType.WeeklyOff : RosterDayType.Shift; day.PunchCount = punches.Count; day.ProcessedAtUtc = now; day.LeaveConflict = leave && punches.Count > 0;
        day.ProcessingOutcome = null; day.FirstPunchAtUtc = adjustment?.EffectiveInAtUtc ?? punches.FirstOrDefault()?.PunchAtUtc; day.LastPunchAtUtc = adjustment?.EffectiveOutAtUtc ?? punches.LastOrDefault()?.PunchAtUtc; day.SessionCount = 0; day.WorkedMinutes = null; day.BreakMinutes = null; day.IsLateIn = false; day.IsEarlyOut = false; day.IsGraceApplied = false; day.IsSinglePunch = punches.Count == 1 && adjustment is null; day.HasMissingInPunch = false; day.HasMissingOutPunch = false; day.HasInvalidPunchSequence = false; day.RequiresMarkOutApproval = false;

        if (day.RosterDayType == RosterDayType.Holiday || day.RosterDayType == RosterDayType.WeeklyOff)
        { day.Status = day.RosterDayType == RosterDayType.Holiday ? EmployeeAttendanceDayStatus.Holiday : EmployeeAttendanceDayStatus.WeeklyOff; day.ProcessingOutcome = "Non-working calendar day; raw punches preserved."; }
        else if (shift is null)
        { day.Status = EmployeeAttendanceDayStatus.NotProcessed; day.ProcessingOutcome = "NoEffectiveShift: attendance was not finalized."; }
        else
        {
            var sessions = adjustment is not null && day.FirstPunchAtUtc is DateTime adjustedIn && day.LastPunchAtUtc is DateTime adjustedOut && adjustedOut >= adjustedIn
                ? [new AttendancePunchSession(adjustedIn, adjustedOut)]
                : Pair(punches, day);
            day.SessionCount = sessions.Count;
            day.BreakMinutes = shift.Breaks.Where(x => !x.IsPaid).Sum(BreakMinutes);
            day.WorkedMinutes = sessions.Count == 0 ? null : Math.Max(0, sessions.Sum(x => x.WorkedMinutes) - day.BreakMinutes.Value);
            var start = businessDate.ToDateTime(shift.StartTime, DateTimeKind.Utc); var end = businessDate.ToDateTime(shift.EndTime, DateTimeKind.Utc); if (shift.CrossesMidnight || shift.EndTime <= shift.StartTime) end = end.AddDays(1); day.ScheduledStartUtc = start; day.ScheduledEndUtc = end;
            if (day.FirstPunchAtUtc is DateTime first)
            { day.IsGraceApplied = first > start && first <= start.AddMinutes(shift.GraceInMinutes); day.IsLateIn = first > start.AddMinutes(shift.GraceInMinutes); }
            if (day.LastPunchAtUtc is DateTime last)
            { day.IsEarlyOut = last < end.AddMinutes(-shift.GraceOutMinutes); day.RequiresMarkOutApproval = last > end && shift.PostShiftMarkOutMode == PostShiftMarkOutMode.RequiresApprovalBeyondLimit && last > end.AddMinutes(shift.MaximumPostShiftMinutes); }
            var beforeEnd = now < end.AddMinutes(Math.Max(shift.GraceOutMinutes, shift.MaximumPostShiftMinutes));
            if (punches.Count == 0 && adjustment is null) { day.Status = leave ? EmployeeAttendanceDayStatus.OnLeave : beforeEnd ? EmployeeAttendanceDayStatus.NotProcessed : EmployeeAttendanceDayStatus.Absent; day.ProcessingOutcome = leave ? "Approved Leave applies; no punch was required." : beforeEnd ? "Shift has not reached its finalization point." : "No qualifying punch was recorded."; }
            else if (day.IsSinglePunch && !shift.AllowPresentOnSinglePunch) { day.Status = EmployeeAttendanceDayStatus.Incomplete; day.HasMissingInPunch |= punches[0].Direction == PunchDirection.Out; day.HasMissingOutPunch |= punches[0].Direction == PunchDirection.In; day.ProcessingOutcome = "A complete In/Out pair is required by the effective Shift."; }
            else if (day.HasInvalidPunchSequence || (shift.IsMarkOutMandatory && day.HasMissingOutPunch)) { day.Status = EmployeeAttendanceDayStatus.Incomplete; day.ProcessingOutcome = "Punch sequence is incomplete or invalid."; }
            else { day.Status = EmployeeAttendanceDayStatus.Present; day.ProcessingOutcome = day.LeaveConflict ? "Attendance present with approved Leave conflict." : "Attendance processed."; }
        }
        if (onDuty)
        { day.Status = EmployeeAttendanceDayStatus.OnDuty; day.ProcessingOutcome = "Approved On Duty applies; raw punches remain visible."; }
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            db.ClearChangeTracker();
            var persisted = await db.EmployeeAttendanceDays.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId && x.EmployeeId == employeeId && x.BusinessDate == businessDate, cancellationToken);
            if (persisted is null) throw;
            day = persisted;
        }
        return Result<EmployeeAttendanceDayDto>.Success(ToDto(day));
    }

    private static List<AttendancePunchSession> Pair(IReadOnlyList<AttendancePunch> punches, EmployeeAttendanceDay day)
    {
        var sessions = new List<AttendancePunchSession>(); AttendancePunch? open = null;
        foreach (var punch in punches)
        {
            if (punch.Direction == PunchDirection.In) { if (open is not null) day.HasInvalidPunchSequence = true; else open = punch; }
            else if (open is null) day.HasInvalidPunchSequence = true;
            else { if (punch.PunchAtUtc < open.PunchAtUtc) day.HasInvalidPunchSequence = true; else sessions.Add(new(open.PunchAtUtc, punch.PunchAtUtc)); open = null; }
        }
        if (open is not null) { day.HasMissingOutPunch = true; day.HasInvalidPunchSequence |= sessions.Count > 0; }
        day.HasMissingInPunch = punches.Any() && punches[0].Direction == PunchDirection.Out;
        return sessions;
    }

    private static int BreakMinutes(ShiftBreak x) { var start = x.StartTime.ToTimeSpan(); var end = x.EndTime.ToTimeSpan(); var minutes = (int)(end - start).TotalMinutes; return minutes >= 0 ? minutes : minutes + 24 * 60; }
    private static EmployeeAttendanceDayDto ToDto(EmployeeAttendanceDay x) => new(x.Id, x.EmployeeId, x.BusinessDate, x.ShiftId, x.ShiftCode, x.ScheduledStartUtc, x.ScheduledEndUtc, x.ExpectedWorkMinutes, x.RosterAssignmentSource, x.RosterDayType, x.Status, x.FirstPunchAtUtc, x.LastPunchAtUtc, x.PunchCount, x.SessionCount, x.WorkedMinutes, x.BreakMinutes, x.IsLateIn, x.IsEarlyOut, x.IsGraceApplied, x.IsSinglePunch, x.HasMissingInPunch, x.HasMissingOutPunch, x.LeaveConflict, x.RequiresMarkOutApproval, x.HasInvalidPunchSequence, x.ProcessedAtUtc, x.ProcessingOutcome);
}
