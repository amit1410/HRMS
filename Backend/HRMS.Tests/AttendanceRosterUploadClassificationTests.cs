using System.Text;
using HRMS.Application.DTOs.Attendance;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class AttendanceRosterUploadClassificationTests
{
    [Fact]
    public async Task Upload_classifies_new_update_unchanged_and_duplicate_rows()
    {
        using var f = await AttendanceTestFixture.CreateAsync();
        var date = new DateOnly(2026, 10, 15);
        var morning = await f.AddShiftAsync("UPLOAD-MORNING");
        var evening = await f.AddShiftAsync("UPLOAD-EVENING");
        await f.Service.AssignRosterAsync(new() { EmployeeIds = [f.EmployeeId], FromDate = date, ToDate = date, ShiftId = morning.Id, DayType = RosterDayType.Shift });

        var update = await Validate(f, $"E001,{date:yyyy-MM-dd},{evening.ShiftCode},Shift");
        Assert.Equal(RosterUploadAction.Update, update.Rows.Single().Action);
        Assert.Equal("UPLOAD-MORNING", update.Rows.Single().CurrentShiftCode);
        Assert.Equal(RosterDayType.Shift, update.Rows.Single().CurrentDayType);
        Assert.Equal("UPLOAD-EVENING", update.Rows.Single().ShiftCode);

        var unchanged = await Validate(f, $"E001,{date:yyyy-MM-dd},{morning.ShiftCode},Shift");
        Assert.Equal(RosterUploadAction.Unchanged, unchanged.Rows.Single().Action);

        var duplicate = await Validate(f, $"E001,{date:yyyy-MM-dd},{evening.ShiftCode},Shift\nE001,{date:yyyy-MM-dd},{evening.ShiftCode},Shift");
        Assert.Equal(RosterUploadStatus.Failed, duplicate.Status);
        Assert.All(duplicate.Rows, row => Assert.Equal(RosterUploadAction.Error, row.Action));
    }

    [Fact]
    public async Task Holiday_upload_exposes_calendar_override_metadata()
    {
        using var f = await AttendanceTestFixture.CreateAsync();
        var date = new DateOnly(2026, 8, 15);
        await f.AddHolidayAsync(date);
        var shift = await f.AddShiftAsync("UPLOAD-HOLIDAY");

        var batch = await Validate(f, $"E001,{date:yyyy-MM-dd},{shift.ShiftCode},Shift", useCalendar: true);
        var row = batch.Rows.Single();

        Assert.Equal(RosterUploadAction.New, row.Action);
        Assert.Equal(RosterCalendarDayType.Holiday, row.UnderlyingCalendarDayType);
        Assert.True(row.WillOverrideCalendar);
        Assert.Equal(RosterDayType.Shift, row.DayType);
    }

    [Fact]
    public async Task Weekly_off_upload_exposes_override_metadata_in_both_directions()
    {
        using var f = await AttendanceTestFixture.CreateAsync();
        var offDate = new DateOnly(2026, 8, 16);
        await f.AddWeeklyOffAsync(DayOfWeek.Sunday);
        var shift = await f.AddShiftAsync("UPLOAD-WEEKLY");

        var toShift = await Validate(f, $"E001,{offDate:yyyy-MM-dd},{shift.ShiftCode},Shift", useCalendar: true);
        Assert.Equal(RosterCalendarDayType.WeeklyOff, toShift.Rows.Single().UnderlyingCalendarDayType);
        Assert.True(toShift.Rows.Single().WillOverrideCalendar);

        var workingDate = new DateOnly(2026, 8, 17);
        await f.Service.AssignRosterAsync(new() { EmployeeIds = [f.EmployeeId], FromDate = workingDate, ToDate = workingDate, ShiftId = shift.Id, DayType = RosterDayType.Shift });
        var toOff = await Validate(f, $"E001,{workingDate:yyyy-MM-dd},,WeeklyOff", useCalendar: true);
        Assert.Equal(RosterDayType.WeeklyOff, toOff.Rows.Single().DayType);
        Assert.True(toOff.Rows.Single().WillOverrideCalendar);
    }

    [Fact]
    public async Task Upload_classifies_invalid_shift_as_error()
    {
        using var f = await AttendanceTestFixture.CreateAsync();
        var batch = await Validate(f, "E001,2026-10-15,DOES-NOT-EXIST,Shift", useCalendar: false);

        Assert.Equal(RosterUploadStatus.Failed, batch.Status);
        Assert.Equal(RosterUploadAction.Error, batch.Rows.Single().Action);
        Assert.Contains("not found", batch.Rows.Single().ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Holiday_override_upload_is_unchanged_and_does_not_add_history()
    {
        using var f = await AttendanceTestFixture.CreateAsync();
        var date = new DateOnly(2026, 8, 15);
        await f.AddHolidayAsync(date);
        var shift = await f.AddShiftAsync("UPLOAD-NOOP");
        await f.CalendarService.AssignRosterAsync(new() { EmployeeIds = [f.EmployeeId], FromDate = date, ToDate = date, ShiftId = shift.Id, DayType = RosterDayType.Shift });
        var before = await f.Context.EmployeeRosterChangeHistories.CountAsync();

        var batch = await Validate(f, $"E001,{date:yyyy-MM-dd},{shift.ShiftCode},Shift", useCalendar: true);

        Assert.Equal(RosterUploadAction.Unchanged, batch.Rows.Single().Action);
        Assert.Equal(RosterCalendarDayType.Holiday, batch.Rows.Single().UnderlyingCalendarDayType);
        Assert.True(batch.Rows.Single().IsCurrentCalendarOverride);
        Assert.Equal(before, await f.Context.EmployeeRosterChangeHistories.CountAsync());
    }

    private static async Task<RosterUploadBatchDto> Validate(AttendanceTestFixture f, string rows, bool useCalendar = false)
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes($"EmployeeCode,Date,ShiftCode,DayType\n{rows}\n"));
        var service = useCalendar ? f.CalendarService : f.Service;
        var result = await service.ValidateRosterUploadAsync("roster.csv", stream);
        Assert.True(result.Succeeded, result.Message);
        return result.Value!;
    }
}
