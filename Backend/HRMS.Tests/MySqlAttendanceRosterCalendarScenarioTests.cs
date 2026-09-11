using System.Text;
using HRMS.Application.DTOs.Attendance;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit.Sdk;

namespace HRMS.Tests;

public sealed class MySqlAttendanceRosterCalendarScenarioTests
{
    [Fact]
    public async Task Holiday_without_override_resolves_as_holiday() => await Run(async s =>
    {
        var result = await s.Service.ResolveAsync(s.Fixture.EmployeeId, s.Holiday);
        Assert.Contains("Holiday", result.Value!.Message);
        Assert.Null(result.Value.ShiftId);
    });

    [Fact]
    public async Task Holiday_override_create_update_remove_preserves_history() => await Run(async s =>
    {
        await s.Assign(s.Holiday, s.Morning, RosterDayType.Shift, "create");
        await s.Assign(s.Holiday, s.Evening, RosterDayType.Shift, "update");
        await s.Service.RemoveRosterAsync(s.Fixture.EmployeeId, s.Holiday);

        var history = await s.Db.EmployeeRosterChangeHistories.Where(x => x.RosterDate == s.Holiday).OrderBy(x => x.ChangedAtUtc).ToListAsync();
        Assert.Collection(history,
            x => { Assert.Equal(RosterChangeType.Created, x.ChangeType); Assert.Equal(s.Morning.Id, x.NewShiftId); Assert.Equal(RosterCalendarDayType.Holiday, x.OriginalCalendarDayType); Assert.Equal(s.Fixture.EmployeeUserId, x.ChangedByUserId); },
            x => { Assert.Equal(RosterChangeType.Updated, x.ChangeType); Assert.Equal(s.Morning.Id, x.PreviousShiftId); Assert.Equal(s.Evening.Id, x.NewShiftId); Assert.True(x.PreviousIsCalendarOverride); Assert.True(x.NewIsCalendarOverride); },
            x => { Assert.Equal(RosterChangeType.Removed, x.ChangeType); Assert.Equal(s.Evening.Id, x.PreviousShiftId); Assert.True(x.PreviousIsCalendarOverride); Assert.Equal(RosterCalendarDayType.Holiday, x.OriginalCalendarDayType); Assert.True(x.ChangedAtUtc.Kind is DateTimeKind.Utc or DateTimeKind.Unspecified); });
        Assert.Contains("Holiday", (await s.Service.ResolveAsync(s.Fixture.EmployeeId, s.Holiday)).Value!.Message);
    });

    [Fact]
    public async Task Weekly_off_shift_override_and_removal_restore_weekly_off() => await Run(async s =>
    {
        await s.Assign(s.WeeklyOff, s.Morning, RosterDayType.Shift, "weekly shift");
        Assert.Equal(s.Morning.Id, (await s.Service.ResolveAsync(s.Fixture.EmployeeId, s.WeeklyOff)).Value!.ShiftId);
        await s.Service.RemoveRosterAsync(s.Fixture.EmployeeId, s.WeeklyOff);
        Assert.Contains("WeeklyOff", (await s.Service.ResolveAsync(s.Fixture.EmployeeId, s.WeeklyOff)).Value!.Message);
    });

    [Fact]
    public async Task Working_shift_can_be_overridden_to_weekly_off_and_removed() => await Run(async s =>
    {
        s.Db.ShiftApplicabilityRules.Add(new ShiftApplicabilityRule { Id = Guid.NewGuid(), TenantId = s.Fixture.TenantId, EmployeeId = s.Fixture.EmployeeId, ShiftId = s.Morning.Id, EffectiveFrom = s.Working });
        await s.Db.SaveChangesAsync();
        await s.Assign(s.Working, null, RosterDayType.WeeklyOff, "weekly off");
        Assert.Null((await s.Service.ResolveAsync(s.Fixture.EmployeeId, s.Working)).Value!.ShiftId);
        await s.Service.RemoveRosterAsync(s.Fixture.EmployeeId, s.Working);
        Assert.Equal(s.Morning.Id, (await s.Service.ResolveAsync(s.Fixture.EmployeeId, s.Working)).Value!.ShiftId);
    });

    [Fact]
    public async Task Upload_new_update_unchanged_and_error_are_persisted_correctly() => await Run(async s =>
    {
        var newBatch = await s.Validate(s.Working, s.Morning.ShiftCode);
        Assert.Equal(RosterUploadAction.New, newBatch.Rows.Single().Action);
        Assert.True((await s.Service.CommitRosterUploadAsync(newBatch.Id)).Succeeded);
        var update = await s.Validate(s.Working, s.Evening.ShiftCode);
        Assert.Equal(RosterUploadAction.Update, update.Rows.Single().Action);
        Assert.Equal(s.Morning.ShiftCode, update.Rows.Single().CurrentShiftCode);
        Assert.Equal(s.Evening.ShiftCode, update.Rows.Single().ShiftCode);
        await s.Service.CommitRosterUploadAsync(update.Id);
        var unchanged = await s.Validate(s.Working, s.Evening.ShiftCode);
        Assert.Equal(RosterUploadAction.Unchanged, unchanged.Rows.Single().Action);
        var before = await s.Db.EmployeeRosterChangeHistories.CountAsync();
        await s.Service.CommitRosterUploadAsync(unchanged.Id);
        Assert.Equal(before, await s.Db.EmployeeRosterChangeHistories.CountAsync());
        var error = await s.Validate(s.Working.AddDays(1), "MISSING-SHIFT");
        Assert.Equal(RosterUploadAction.Error, error.Rows.Single().Action);
        Assert.Equal(RosterUploadStatus.Failed, error.Status);
    });

    [Fact]
    public async Task Holiday_and_weekly_off_uploads_expose_calendar_metadata() => await Run(async s =>
    {
        var holiday = await s.Validate(s.Holiday, s.Morning.ShiftCode);
        Assert.Equal(RosterCalendarDayType.Holiday, holiday.Rows.Single().UnderlyingCalendarDayType);
        Assert.True(holiday.Rows.Single().WillOverrideCalendar);
        Assert.Equal(RosterDayType.Shift, holiday.Rows.Single().DayType);
        var weekly = await s.Validate(s.WeeklyOff, s.Morning.ShiftCode);
        Assert.Equal(RosterCalendarDayType.WeeklyOff, weekly.Rows.Single().UnderlyingCalendarDayType);
        Assert.True(weekly.Rows.Single().WillOverrideCalendar);
        var workingOff = await s.Validate(s.Working, null, RosterDayType.WeeklyOff);
        Assert.Equal(RosterDayType.WeeklyOff, workingOff.Rows.Single().DayType);
        Assert.Null(workingOff.Rows.Single().ShiftCode);
        Assert.True(workingOff.Rows.Single().WillOverrideCalendar);
    });

    [Fact]
    public async Task Duplicate_upload_rows_are_rejected_and_do_not_commit() => await Run(async s =>
    {
        var csv = $"EmployeeCode,Date,ShiftCode,DayType\n{s.Fixture.EmployeeCode},{s.Working:yyyy-MM-dd},{s.Morning.ShiftCode},Shift\n{s.Fixture.EmployeeCode},{s.Working:yyyy-MM-dd},{s.Evening.ShiftCode},Shift\n";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var result = await s.Service.ValidateRosterUploadAsync("duplicate.csv", stream);
        Assert.True(result.Succeeded);
        Assert.Equal(RosterUploadStatus.Failed, result.Value!.Status);
        Assert.All(result.Value.Rows, row => Assert.Equal(RosterUploadAction.Error, row.Action));
        Assert.False(await s.Db.EmployeeRosterDays.AnyAsync(x => x.EmployeeId == s.Fixture.EmployeeId && x.RosterDate == s.Working));
    });

    [Fact]
    public async Task Upload_and_direct_roster_operations_are_tenant_isolated() => await Run(async s =>
    {
        await using var otherDb = s.Fixture.CreateContext(new TestTenantContext(s.Fixture.OtherTenantId, Guid.NewGuid()));
        var otherService = new AttendanceFoundationService(otherDb, new TestTenantContext(s.Fixture.OtherTenantId, Guid.NewGuid()), new EffectiveEmploymentResolver(otherDb, new TestTenantContext(s.Fixture.OtherTenantId, Guid.NewGuid())));
        var direct = await otherService.AssignRosterAsync(new() { EmployeeIds = [s.Fixture.EmployeeId], FromDate = s.Working, ToDate = s.Working, ShiftId = s.Morning.Id, DayType = RosterDayType.Shift });
        Assert.False(direct.Succeeded);
        Assert.False(await otherDb.EmployeeRosterDays.AnyAsync(x => x.EmployeeId == s.Fixture.EmployeeId));
        var csv = $"EmployeeCode,Date,ShiftCode,DayType\n{s.Fixture.EmployeeCode},{s.Working:yyyy-MM-dd},{s.Morning.ShiftCode},Shift\n";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var upload = await otherService.ValidateRosterUploadAsync("cross-tenant.csv", stream);
        Assert.True(upload.Succeeded);
        Assert.Equal(RosterUploadAction.Error, upload.Value!.Rows.Single().Action);
    });

    private static async Task Run(Func<Scenario, Task> test)
    {
        var connection = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) throw SkipException.ForSkip("MySQL roster/calendar tests require HRMS_MYSQL_TEST_CONNECTION.");
        var fixture = new MySqlLeaveLifecycleIntegrationTests.Fixture(connection);
        try
        {
            await fixture.SeedAsync();
            await using var setup = fixture.CreateContext(new TestTenantContext(fixture.TenantId));
            var holiday = new DateOnly(2026, 8, 15); var weekly = new DateOnly(2026, 8, 16);
            setup.Holidays.Add(new Holiday { Id = Guid.NewGuid(), TenantId = fixture.TenantId, Name = "Attendance Holiday", Date = holiday });
            var configuration = new WeeklyOffConfiguration { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EffectiveFrom = new(2026, 1, 1), EffectiveTo = new(2026, 12, 31) };
            configuration.Days.Add(new WeeklyOffDay { Id = Guid.NewGuid(), TenantId = fixture.TenantId, WeeklyOffConfigurationId = configuration.Id, DayOfWeek = DayOfWeek.Sunday }); setup.WeeklyOffConfigurations.Add(configuration); await setup.SaveChangesAsync();
            await using var db = fixture.CreateContext(); var employment = new EffectiveEmploymentResolver(db, fixture.EmployeeTenant); var service = new AttendanceFoundationService(db, fixture.EmployeeTenant, employment, new WorkingDayCalendarResolver(db, fixture.EmployeeTenant, employment));
            var morning = await AddShift(db, fixture, "M"); var evening = await AddShift(db, fixture, "E");
            await test(new Scenario(fixture, db, service, morning, evening, holiday, weekly, new(2026, 8, 17)));
        }
        finally { await fixture.CleanupAttendanceAsync(); await fixture.CleanupAsync(); }
    }

    private static async Task<Shift> AddShift(HRMS.Infrastructure.Persistence.HrmsDbContext db, MySqlLeaveLifecycleIntegrationTests.Fixture fixture, string prefix)
    {
        var shift = new Shift { Id = Guid.NewGuid(), TenantId = fixture.TenantId, ShiftCode = $"{prefix}{fixture.TenantId:N}"[..8], ShiftName = prefix, EffectiveFrom = new(2026, 1, 1), StartTime = new(9, 0), EndTime = new(18, 0), PlannedDurationMinutes = 540, FullDayWorkMinutes = 480, MinimumWorkMinutes = 480 };
        db.Shifts.Add(shift); await db.SaveChangesAsync(); return shift;
    }

    private sealed record Scenario(MySqlLeaveLifecycleIntegrationTests.Fixture Fixture, HRMS.Infrastructure.Persistence.HrmsDbContext Db, AttendanceFoundationService Service, Shift Morning, Shift Evening, DateOnly Holiday, DateOnly WeeklyOff, DateOnly Working)
    {
        public async Task Assign(DateOnly date, Shift? shift, RosterDayType dayType, string reason) => Assert.True((await Service.AssignRosterAsync(new() { EmployeeIds = [Fixture.EmployeeId], FromDate = date, ToDate = date, ShiftId = shift?.Id, DayType = dayType, Reason = reason })).Succeeded);
        public async Task<RosterUploadBatchDto> Validate(DateOnly date, string? shiftCode, RosterDayType dayType = RosterDayType.Shift)
        {
            var csv = $"EmployeeCode,Date,ShiftCode,DayType\n{Fixture.EmployeeCode},{date:yyyy-MM-dd},{shiftCode ?? string.Empty},{dayType}\n";
            await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv)); var result = await Service.ValidateRosterUploadAsync("roster.csv", stream); Assert.True(result.Succeeded, result.Message); return result.Value!;
        }
    }
}
