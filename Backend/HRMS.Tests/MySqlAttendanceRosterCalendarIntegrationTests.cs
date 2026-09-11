using System.Text;
using HRMS.Application.DTOs.Attendance;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit.Sdk;

namespace HRMS.Tests;

public sealed class MySqlAttendanceRosterCalendarIntegrationTests
{
    [Fact]
    public async Task Real_mysql_proves_roster_calendar_history_and_upload_metadata()
    {
        var connection = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) throw SkipException.ForSkip("MySQL roster/calendar tests not executed: HRMS_MYSQL_TEST_CONNECTION is absent.");
        var fixture = new MySqlLeaveLifecycleIntegrationTests.Fixture(connection);
        try
        {
            await fixture.SeedAsync();
            var holiday = new DateOnly(2026, 8, 15);
            var weeklyOff = new DateOnly(2026, 8, 16);
            await using (var setup = fixture.CreateContext(new TestTenantContext(fixture.TenantId)))
            {
                setup.Holidays.Add(new Holiday { Id = Guid.NewGuid(), TenantId = fixture.TenantId, Name = "Attendance Holiday", Date = holiday });
                var weekly = new WeeklyOffConfiguration { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EffectiveFrom = new(2026, 1, 1), EffectiveTo = new(2026, 12, 31) };
                weekly.Days.Add(new WeeklyOffDay { Id = Guid.NewGuid(), TenantId = fixture.TenantId, WeeklyOffConfigurationId = weekly.Id, DayOfWeek = DayOfWeek.Sunday });
                setup.WeeklyOffConfigurations.Add(weekly);
                await setup.SaveChangesAsync();
            }

            await using var db = fixture.CreateContext();
            var employment = new EffectiveEmploymentResolver(db, fixture.EmployeeTenant);
            var calendar = new WorkingDayCalendarResolver(db, fixture.EmployeeTenant, employment);
            var service = new AttendanceFoundationService(db, fixture.EmployeeTenant, employment, calendar);
            var morning = await AddShift(db, fixture.TenantId, $"M{fixture.TenantId:N}"[..8]);
            var evening = await AddShift(db, fixture.TenantId, $"E{fixture.TenantId:N}"[..8]);

            Assert.Contains("Holiday", (await service.ResolveAsync(fixture.EmployeeId, holiday)).Value!.Message);
            var created = await service.AssignRosterAsync(new() { EmployeeIds = [fixture.EmployeeId], FromDate = holiday, ToDate = holiday, ShiftId = morning.Id, DayType = RosterDayType.Shift, Reason = "Holiday coverage" });
            Assert.True(created.Succeeded, created.Message);
            var current = await db.EmployeeRosterDays.SingleAsync(x => x.EmployeeId == fixture.EmployeeId && x.RosterDate == holiday);
            Assert.True(current.IsCalendarOverride);
            Assert.Equal(RosterCalendarDayType.Holiday, current.OriginalCalendarDayType);
            Assert.Equal(fixture.EmployeeUserId, await db.EmployeeRosterChangeHistories.Where(x => x.RosterDate == holiday).Select(x => x.ChangedByUserId).SingleAsync());

            await service.AssignRosterAsync(new() { EmployeeIds = [fixture.EmployeeId], FromDate = holiday, ToDate = holiday, ShiftId = evening.Id, DayType = RosterDayType.Shift, Reason = "Coverage update" });
            Assert.Equal(evening.Id, await db.EmployeeRosterDays.Where(x => x.RosterDate == holiday).Select(x => x.ShiftId).SingleAsync());
            Assert.Equal(2, await db.EmployeeRosterChangeHistories.CountAsync(x => x.RosterDate == holiday));
            await service.RemoveRosterAsync(fixture.EmployeeId, holiday);
            Assert.Contains("Holiday", (await service.ResolveAsync(fixture.EmployeeId, holiday)).Value!.Message);
            Assert.Equal(RosterChangeType.Removed, await db.EmployeeRosterChangeHistories.Where(x => x.RosterDate == holiday).OrderBy(x => x.ChangedAtUtc).Select(x => x.ChangeType).LastAsync());

            await service.AssignRosterAsync(new() { EmployeeIds = [fixture.EmployeeId], FromDate = weeklyOff, ToDate = weeklyOff, ShiftId = morning.Id, DayType = RosterDayType.Shift });
            Assert.Equal(morning.Id, (await service.ResolveAsync(fixture.EmployeeId, weeklyOff)).Value!.ShiftId);
            await service.RemoveRosterAsync(fixture.EmployeeId, weeklyOff);
            Assert.Contains("WeeklyOff", (await service.ResolveAsync(fixture.EmployeeId, weeklyOff)).Value!.Message);

            var working = new DateOnly(2026, 8, 17);
            db.ShiftApplicabilityRules.Add(new ShiftApplicabilityRule { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, ShiftId = morning.Id, EffectiveFrom = working });
            await db.SaveChangesAsync();
            await service.AssignRosterAsync(new() { EmployeeIds = [fixture.EmployeeId], FromDate = working, ToDate = working, ShiftId = morning.Id, DayType = RosterDayType.Shift });
            await service.AssignRosterAsync(new() { EmployeeIds = [fixture.EmployeeId], FromDate = working, ToDate = working, DayType = RosterDayType.WeeklyOff });
            Assert.Null((await service.ResolveAsync(fixture.EmployeeId, working)).Value!.ShiftId);
            await service.RemoveRosterAsync(fixture.EmployeeId, working);
            Assert.Equal(morning.Id, (await service.ResolveAsync(fixture.EmployeeId, working)).Value!.ShiftId);

            var holidayUpload = await Validate(service, fixture.EmployeeCode, holiday, morning.ShiftCode);
            Assert.Equal(RosterUploadAction.New, holidayUpload.Rows.Single().Action);
            Assert.True(holidayUpload.Rows.Single().WillOverrideCalendar);
            Assert.Equal(RosterCalendarDayType.Holiday, holidayUpload.Rows.Single().UnderlyingCalendarDayType);
            var committed = await service.CommitRosterUploadAsync(holidayUpload.Id);
            Assert.True(committed.Succeeded, committed.Message);
            var unchanged = await Validate(service, fixture.EmployeeCode, holiday, morning.ShiftCode);
            Assert.Equal(RosterUploadAction.Unchanged, unchanged.Rows.Single().Action);
            Assert.Equal(4, await db.EmployeeRosterChangeHistories.CountAsync(x => x.RosterDate == holiday));
            var duplicate = await Validate(service, fixture.EmployeeCode, new DateOnly(2026, 8, 18), morning.ShiftCode, $"{fixture.EmployeeCode},2026-08-18,{morning.ShiftCode},Shift");
            Assert.Equal(RosterUploadStatus.Failed, duplicate.Status);
            Assert.All(duplicate.Rows, x => Assert.Equal(RosterUploadAction.Error, x.Action));
        }
        finally
        {
            await fixture.CleanupAttendanceAsync();
            await fixture.CleanupAsync();
        }
    }

    private static async Task<Shift> AddShift(HRMS.Infrastructure.Persistence.HrmsDbContext db, Guid tenantId, string code)
    {
        var shift = new Shift { Id = Guid.NewGuid(), TenantId = tenantId, ShiftCode = code, ShiftName = code, EffectiveFrom = new(2026, 1, 1), StartTime = new(9, 0), EndTime = new(18, 0), PlannedDurationMinutes = 540, FullDayWorkMinutes = 480, MinimumWorkMinutes = 480, IsActive = true };
        db.Shifts.Add(shift); await db.SaveChangesAsync(); return shift;
    }

    private static async Task<RosterUploadBatchDto> Validate(AttendanceFoundationService service, string employeeCode, DateOnly date, string shiftCode, string? extra = null)
    {
        var csv = $"EmployeeCode,Date,ShiftCode,DayType\n{employeeCode},{date:yyyy-MM-dd},{shiftCode},Shift\n{extra ?? string.Empty}";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var result = await service.ValidateRosterUploadAsync("attendance-calendar.csv", stream);
        Assert.True(result.Succeeded, result.Message);
        return result.Value!;
    }
}
