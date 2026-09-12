using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Attendance;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

public sealed class AttendanceReadService(
    IHrmsDbContext db,
    ITenantContext tenant,
    IAttendanceFoundationService roster,
    IEmployeeIdentityResolver identity,
    IEmployeeManagerResolver managers) : IAttendanceReadService
{
    public async Task<Result<IReadOnlyList<AttendanceCalendarDayDto>>> GetMyCalendarAsync(AttendanceCalendarQuery query, CancellationToken ct = default)
    {
        var subject = await identity.ResolveCurrentAsync(ct);
        if (!subject.Succeeded) return Result<IReadOnlyList<AttendanceCalendarDayDto>>.Failure(subject.Status, subject.Message, subject.Errors);
        if (query.Year is < 1 or > 9999 || query.Month is < 1 or > 12)
            return Result<IReadOnlyList<AttendanceCalendarDayDto>>.Invalid("month", "A valid year and month are required.");
        var from = new DateOnly(query.Year, query.Month, 1);
        var to = from.AddMonths(1).AddDays(-1);
        return await BuildDaysAsync(subject.Value!.EmployeeId, from, to, ct);
    }

    public async Task<Result<AttendanceDayDetailDto>> GetMyDayAsync(DateOnly date, CancellationToken ct = default)
    {
        var subject = await identity.ResolveCurrentAsync(ct);
        if (!subject.Succeeded) return Result<AttendanceDayDetailDto>.Failure(subject.Status, subject.Message, subject.Errors);
        return Result<AttendanceDayDetailDto>.Success(await BuildDetailAsync(subject.Value!.EmployeeId, date, ct));
    }

    public async Task<Result<ManagerAttendanceResult>> GetManagerTeamAsync(ManagerAttendanceQuery query, CancellationToken ct = default)
    {
        var subject = await identity.ResolveCurrentAsync(ct);
        if (!subject.Succeeded) return Result<ManagerAttendanceResult>.Failure(subject.Status, subject.Message, subject.Errors);
        if (query.FromDate > query.ToDate) return Result<ManagerAttendanceResult>.Invalid("dateRange", "FromDate cannot be after ToDate.");
        if (query.ToDate.DayNumber - query.FromDate.DayNumber + 1 > 92) return Result<ManagerAttendanceResult>.Invalid("dateRange", "Attendance team range cannot exceed 92 days.");
        if (query.Page < 1 || query.PageSize is < 1 or > PagedQuery.MaxPageSize) return Result<ManagerAttendanceResult>.Invalid("page", "Page values are out of range.");

        var employees = await db.Employees.AsNoTracking()
            .Where(x => x.TenantId == subject.Value!.TenantId && (query.EmployeeId == null || x.Id == query.EmployeeId))
            .Select(x => new { x.Id, Code = x.EmployeeCode ?? string.Empty, Name = (x.FirstName + " " + x.LastName).Trim(), Department = (string?)null })
            .ToListAsync(ct);
        var rows = new List<ManagerAttendanceRowDto>();
        for (var date = query.FromDate; date <= query.ToDate; date = date.AddDays(1))
        foreach (var employee in employees)
        {
            var manager = await managers.ResolveAsync(employee.Id, date, ct);
            if (manager.Value?.ManagerId != subject.Value.EmployeeId) continue;
            var day = await BuildDayAsync(employee.Id, date, ct);
            if (query.Status is not null && day.AttendanceStatus != query.Status) continue;
            if (query.DayType is not null && day.DayType != query.DayType) continue;
            if (query.ShiftId is not null && day.ShiftId != query.ShiftId) continue;
            if (!MatchesVariance(day, query.Variance)) continue;
            rows.Add(new(day, employee.Id, employee.Code, employee.Name, employee.Department));
        }
        rows = rows.OrderBy(x => x.Day.Date).ThenBy(x => x.EmployeeCode).ToList();
        var summary = new AttendanceSummaryDto(rows.Count(x => x.Day.AttendanceStatus == EmployeeAttendanceDayStatus.Present), rows.Count(x => x.Day.AttendanceStatus == EmployeeAttendanceDayStatus.Absent), rows.Count(x => x.Day.AttendanceStatus == EmployeeAttendanceDayStatus.OnLeave), rows.Count(x => x.Day.AttendanceStatus == EmployeeAttendanceDayStatus.Holiday), rows.Count(x => x.Day.AttendanceStatus == EmployeeAttendanceDayStatus.WeeklyOff), rows.Count(x => x.Day.AttendanceStatus == EmployeeAttendanceDayStatus.Incomplete), rows.Count(x => x.Day.AttendanceStatus == EmployeeAttendanceDayStatus.NotProcessed), rows.Count(x => x.Day.IsLateIn), rows.Count(x => x.Day.IsEarlyOut), rows.Count(x => x.Day.LeaveConflict));
        return Result<ManagerAttendanceResult>.Success(new(new(rows.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToList(), query.Page, query.PageSize, rows.Count), summary));
    }

    public async Task<Result<AttendanceDayDetailDto>> GetManagerDayAsync(Guid employeeId, DateOnly date, CancellationToken ct = default)
    {
        var subject = await identity.ResolveCurrentAsync(ct);
        if (!subject.Succeeded) return Result<AttendanceDayDetailDto>.Failure(subject.Status, subject.Message, subject.Errors);
        var relation = await managers.ResolveAsync(employeeId, date, ct);
        if (!relation.Succeeded || relation.Value?.ManagerId != subject.Value!.EmployeeId)
            return Result<AttendanceDayDetailDto>.Forbidden("The employee is not an effective report for this date.");
        return Result<AttendanceDayDetailDto>.Success(await BuildDetailAsync(employeeId, date, ct));
    }

    private async Task<Result<IReadOnlyList<AttendanceCalendarDayDto>>> BuildDaysAsync(Guid employeeId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var items = new List<AttendanceCalendarDayDto>();
        for (var date = from; date <= to; date = date.AddDays(1)) items.Add(await BuildDayAsync(employeeId, date, ct));
        return Result<IReadOnlyList<AttendanceCalendarDayDto>>.Success(items);
    }

    private async Task<AttendanceDayDetailDto> BuildDetailAsync(Guid employeeId, DateOnly date, CancellationToken ct)
    {
        var day = await BuildDayAsync(employeeId, date, ct);
        var tenantId = tenant.TenantId!.Value;
        var punches = await db.AttendancePunches.AsNoTracking().Where(x => x.TenantId == tenantId && x.EmployeeId == employeeId && x.BusinessDate == date).OrderBy(x => x.PunchAtUtc).ThenBy(x => x.Id).Select(x => new AttendancePunchDto(x.Id, x.EmployeeId, x.PunchAtUtc, x.BusinessDate, x.Direction, x.Source, x.ExternalPunchId, x.DeviceId, x.CapturedAtUtc)).ToListAsync(ct);
        var sessions = new List<AttendancePunchSessionDto>();
        AttendancePunchDto? open = null;
        foreach (var punch in punches) { if (punch.Direction == PunchDirection.In) open ??= punch; else if (open is not null && punch.PunchAtUtc >= open.PunchAtUtc) { sessions.Add(new(open.PunchAtUtc, punch.PunchAtUtc, (int)(punch.PunchAtUtc - open.PunchAtUtc).TotalMinutes)); open = null; } }
        return new(day, punches, sessions);
    }

    private async Task<AttendanceCalendarDayDto> BuildDayAsync(Guid employeeId, DateOnly date, CancellationToken ct)
    {
        var resolution = await roster.ResolveAsync(employeeId, date, ct);
        if (!resolution.Succeeded) return PlannedFallback(employeeId, date, resolution.Message);
        var value = resolution.Value!;
        var existing = await db.EmployeeAttendanceDays.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenant.TenantId && x.EmployeeId == employeeId && x.BusinessDate == date, ct);
        var shift = value.ShiftId is Guid sid ? await db.Shifts.AsNoTracking().Where(x => x.TenantId == tenant.TenantId && x.Id == sid).Select(x => new { x.Id, x.ShiftCode, x.ShiftName }).SingleOrDefaultAsync(ct) : null;
        var holiday = value.Message.StartsWith("Holiday", StringComparison.OrdinalIgnoreCase);
        var weekly = value.PatternDayType == ShiftPatternDayType.WeeklyOff || value.Message.StartsWith("WeeklyOff", StringComparison.OrdinalIgnoreCase);
        var dayType = existing?.RosterDayType ?? (holiday ? RosterDayType.Holiday : weekly ? RosterDayType.WeeklyOff : RosterDayType.Shift);
        var leave = await db.LeaveRequestDays.AsNoTracking().AnyAsync(x => x.TenantId == tenant.TenantId && x.Date == date && x.LeaveRequest != null && x.LeaveRequest.EmployeeId == employeeId && x.LeaveRequest.Status == LeaveRequestStatus.Approved, ct);
        if (existing is not null) return ToCalendar(existing, shift?.ShiftName, RosterCalendarDayTypeFor(dayType, value), existing.IsProcessed(), existing.ProcessingOutcome);
        var status = holiday ? EmployeeAttendanceDayStatus.Holiday : weekly ? EmployeeAttendanceDayStatus.WeeklyOff : leave ? EmployeeAttendanceDayStatus.OnLeave : EmployeeAttendanceDayStatus.NotProcessed;
        return new(date, date.DayOfWeek, dayType, status, shift?.Id, shift?.ShiftCode, shift?.ShiftName, null, null, null, null, null, null, value.Source, value.IsCalendarOverride, RosterCalendarDayTypeFor(dayType, value), false, false, false, false, false, false, false, false, false, 0, 0, false, leave ? "Approved Leave applies." : "Attendance has not been processed.");
    }

    private static AttendanceCalendarDayDto ToCalendar(EmployeeAttendanceDay x, string? shiftName, RosterCalendarDayType calendar, bool processed, string? message) => new(x.BusinessDate, x.BusinessDate.DayOfWeek, x.RosterDayType, x.Status, x.ShiftId, x.ShiftCode, shiftName, x.ScheduledStartUtc, x.ScheduledEndUtc, x.FirstPunchAtUtc, x.LastPunchAtUtc, x.WorkedMinutes, x.ExpectedWorkMinutes, x.RosterAssignmentSource, x.RosterDayType != RosterDayType.Shift || x.RosterAssignmentSource is RosterAssignmentSource.Manual or RosterAssignmentSource.Upload, calendar, x.IsLateIn, x.IsEarlyOut, x.IsGraceApplied, x.IsSinglePunch, x.HasMissingInPunch, x.HasMissingOutPunch, x.HasInvalidPunchSequence, x.LeaveConflict, x.RequiresMarkOutApproval, x.PunchCount, x.SessionCount, processed, message);
    private static AttendanceCalendarDayDto PlannedFallback(Guid employeeId, DateOnly date, string message) => new(date, date.DayOfWeek, RosterDayType.Shift, EmployeeAttendanceDayStatus.NotProcessed, null, null, null, null, null, null, null, null, null, RosterAssignmentSource.System, false, RosterCalendarDayType.WorkingDay, false, false, false, false, false, false, false, false, false, 0, 0, false, message);
    private static RosterCalendarDayType RosterCalendarDayTypeFor(RosterDayType dayType, ShiftResolutionDto value) => dayType switch { RosterDayType.Holiday => RosterCalendarDayType.Holiday, RosterDayType.WeeklyOff => RosterCalendarDayType.WeeklyOff, _ => RosterCalendarDayType.WorkingDay };
    private static bool MatchesVariance(AttendanceCalendarDayDto day, string? variance) => string.IsNullOrWhiteSpace(variance) || variance.Trim().ToLowerInvariant() switch { "late" => day.IsLateIn, "earlyout" or "early-out" => day.IsEarlyOut, "leaveconflict" or "leave-conflict" => day.LeaveConflict, "incomplete" => day.AttendanceStatus == EmployeeAttendanceDayStatus.Incomplete, _ => false };
}

file static class AttendanceDayExtensions
{
    public static bool IsProcessed(this EmployeeAttendanceDay day) => day.ProcessedAtUtc != default;
}
