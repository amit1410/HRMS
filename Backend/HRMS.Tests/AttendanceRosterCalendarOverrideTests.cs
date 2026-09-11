using HRMS.Application.DTOs.Attendance;
using HRMS.API.Controllers;
using HRMS.API.Security;
using HRMS.Domain.Authorization;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class AttendanceRosterCalendarOverrideTests
{
    [Fact]
    public void Roster_mutation_endpoints_require_roster_manage_permission()
    {
        var assign = typeof(AttendanceFoundationController).GetMethod(nameof(AttendanceFoundationController.Assign));
        var remove = typeof(AttendanceFoundationController).GetMethod(nameof(AttendanceFoundationController.RemoveRoster));

        Assert.Equal(Permissions.Attendance.RosterManage, assign!.GetCustomAttributes(typeof(HasPermissionAttribute), false).Cast<HasPermissionAttribute>().Single().Policy);
        Assert.Equal(Permissions.Attendance.RosterManage, remove!.GetCustomAttributes(typeof(HasPermissionAttribute), false).Cast<HasPermissionAttribute>().Single().Policy);
    }

    [Fact]
    public async Task Holiday_falls_back_without_explicit_roster()
    {
        using var f = await AttendanceTestFixture.CreateAsync();
        var date = new DateOnly(2026, 8, 15);
        await f.AddHolidayAsync(date);

        var result = await f.CalendarService.ResolveAsync(f.EmployeeId, date);

        Assert.True(result.Succeeded);
        Assert.Null(result.Value!.ShiftId);
        Assert.Contains("Holiday", result.Value.Message);
    }

    [Fact]
    public async Task Holiday_override_is_persisted_and_recorded()
    {
        using var f = await AttendanceTestFixture.CreateAsync();
        var date = new DateOnly(2026, 8, 15);
        await f.AddHolidayAsync(date);
        var shift = await f.AddShiftAsync("HOL-MORNING");

        var result = await f.CalendarService.AssignRosterAsync(new() { EmployeeIds = [f.EmployeeId], FromDate = date, ToDate = date, ShiftId = shift.Id, DayType = RosterDayType.Shift, Reason = "Holiday coverage" });
        var row = await f.Context.EmployeeRosterDays.SingleAsync();
        var history = await f.Context.EmployeeRosterChangeHistories.SingleAsync();

        Assert.True(result.Succeeded);
        Assert.True(row.IsCalendarOverride);
        Assert.Equal(RosterCalendarDayType.Holiday, row.OriginalCalendarDayType);
        Assert.Equal(RosterChangeType.Created, history.ChangeType);
        Assert.Equal(f.TenantId, history.TenantId);
        Assert.Equal(f.EmployeeId, history.EmployeeId);
        Assert.Equal(RosterCalendarDayType.Holiday, history.OriginalCalendarDayType);
        Assert.True(history.ChangedAtUtc != default);
        Assert.Equal(shift.Id, (await f.CalendarService.ResolveAsync(f.EmployeeId, date)).Value!.ShiftId);
    }

    [Fact]
    public async Task Removing_holiday_override_restores_calendar_fallback_and_keeps_history()
    {
        using var f = await AttendanceTestFixture.CreateAsync();
        var date = new DateOnly(2026, 8, 15);
        await f.AddHolidayAsync(date);
        var shift = await f.AddShiftAsync("HOL-MORNING");
        await f.CalendarService.AssignRosterAsync(new() { EmployeeIds = [f.EmployeeId], FromDate = date, ToDate = date, ShiftId = shift.Id, DayType = RosterDayType.Shift });

        var removed = await f.CalendarService.RemoveRosterAsync(f.EmployeeId, date);
        var resolved = await f.CalendarService.ResolveAsync(f.EmployeeId, date);
        var history = await f.Context.EmployeeRosterChangeHistories.OrderBy(x => x.ChangedAtUtc).ToListAsync();

        Assert.True(removed.Succeeded);
        Assert.Contains("Holiday", resolved.Value!.Message);
        Assert.Empty(f.Context.EmployeeRosterDays);
        Assert.Equal(RosterChangeType.Removed, history[^1].ChangeType);
        Assert.Equal(RosterCalendarDayType.Holiday, history[^1].OriginalCalendarDayType);
    }

    [Fact]
    public async Task Weekly_off_override_and_removal_restore_underlying_calendar()
    {
        using var f = await AttendanceTestFixture.CreateAsync();
        var date = new DateOnly(2026, 8, 16);
        await f.AddWeeklyOffAsync(DayOfWeek.Sunday);
        var shift = await f.AddShiftAsync("WEEKLY-MORNING");

        await f.CalendarService.AssignRosterAsync(new() { EmployeeIds = [f.EmployeeId], FromDate = date, ToDate = date, ShiftId = shift.Id, DayType = RosterDayType.Shift });
        Assert.Equal(shift.Id, (await f.CalendarService.ResolveAsync(f.EmployeeId, date)).Value!.ShiftId);
        await f.CalendarService.RemoveRosterAsync(f.EmployeeId, date);

        var fallback = await f.CalendarService.ResolveAsync(f.EmployeeId, date);
        Assert.Contains("WeeklyOff", fallback.Value!.Message);
    }

    [Fact]
    public async Task Working_day_can_be_overridden_to_weekly_off_and_removed()
    {
        using var f = await AttendanceTestFixture.CreateAsync();
        var date = new DateOnly(2026, 8, 17);
        var shift = await f.AddShiftAsync("WORKING-MORNING");
        await f.AddRuleAsync(shift.Id, departmentId: f.DepartmentId);

        await f.CalendarService.AssignRosterAsync(new() { EmployeeIds = [f.EmployeeId], FromDate = date, ToDate = date, DayType = RosterDayType.WeeklyOff });
        var overridden = await f.CalendarService.ResolveAsync(f.EmployeeId, date);
        Assert.Null(overridden.Value!.ShiftId);
        Assert.True(overridden.Value.IsCalendarOverride);
        await f.CalendarService.RemoveRosterAsync(f.EmployeeId, date);

        var restored = await f.CalendarService.ResolveAsync(f.EmployeeId, date);
        Assert.Equal(shift.Id, restored.Value!.ShiftId);
    }

    [Fact]
    public async Task Identical_manual_assignment_is_a_no_op_without_extra_history()
    {
        using var f = await AttendanceTestFixture.CreateAsync();
        var date = new DateOnly(2026, 8, 17);
        var shift = await f.AddShiftAsync("NOOP-MORNING");
        var request = new RosterAssignmentRequest { EmployeeIds = [f.EmployeeId], FromDate = date, ToDate = date, ShiftId = shift.Id, DayType = RosterDayType.Shift };

        await f.Service.AssignRosterAsync(request);
        await f.Service.AssignRosterAsync(request);

        Assert.Single(f.Context.EmployeeRosterChangeHistories);
    }
}
