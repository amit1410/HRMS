using HRMS.Application.DTOs.Attendance;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit.Sdk;

namespace HRMS.Tests;

public sealed class MySqlAttendanceRosterQueryIntegrationTests
{
    [Fact]
    public async Task Real_mysql_effective_roster_query_returns_a_tenant_scoped_default_row()
    {
        var connection = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) throw SkipException.ForSkip("MySQL Attendance roster-query tests not executed: connection is absent.");
        var fixture = new MySqlLeaveLifecycleIntegrationTests.Fixture(connection);
        try
        {
            await fixture.SeedAsync();
            await using var db = fixture.CreateContext();
            var shift = new Shift { Id = Guid.NewGuid(), TenantId = fixture.TenantId, ShiftCode = $"GRID-{Guid.NewGuid():N}"[..12], ShiftName = "Grid default", IsDefault = true, EffectiveFrom = new(2026, 1, 1), StartTime = new(9, 0), EndTime = new(18, 0), PlannedDurationMinutes = 540, FullDayWorkMinutes = 480, MinimumWorkMinutes = 480 };
            db.Shifts.Add(shift); await db.SaveChangesAsync();
            var employment = new EffectiveEmploymentResolver(db, fixture.EmployeeTenant);
            var service = new AttendanceFoundationService(db, fixture.EmployeeTenant, employment, new WorkingDayCalendarResolver(db, fixture.EmployeeTenant, employment));
            var result = await service.GetRosterAsync(new RosterQuery { FromDate = new(2026, 10, 1), ToDate = new(2026, 10, 1), EmployeeId = fixture.EmployeeId, PageSize = 10 });
            var row = Assert.Single(result.Value!.Items);
            Assert.Equal(fixture.EmployeeId, row.EmployeeId);
            Assert.Equal(shift.Id, row.EffectiveShiftId);
            Assert.Equal(RosterAssignmentSource.System, row.AssignmentSource);
            Assert.False(row.HasExplicitRoster);
        }
        finally
        {
            await using var cleanup = fixture.CreateContext(new TestTenantContext(fixture.OtherTenantId));
            await cleanup.Shifts.Where(x => x.TenantId == fixture.OtherTenantId).ExecuteDeleteAsync();
            await cleanup.EmployeeEmploymentHistory.Where(x => x.TenantId == fixture.OtherTenantId).ExecuteDeleteAsync();
            await cleanup.Employees.Where(x => x.TenantId == fixture.OtherTenantId).ExecuteDeleteAsync();
            await fixture.CleanupAttendanceAsync(); await fixture.CleanupAsync();
        }
    }

    [Fact]
    public async Task Real_mysql_roster_query_isolated_tenant_to_tenant()
    {
        var connection = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) throw SkipException.ForSkip("MySQL Attendance roster-query tests not executed: connection is absent.");
        var fixture = new MySqlLeaveLifecycleIntegrationTests.Fixture(connection);
        try
        {
            await fixture.SeedAsync();
            await using var other = fixture.CreateContext(new TestTenantContext(fixture.OtherTenantId));
            var employee = new Employee { Id = Guid.NewGuid(), TenantId = fixture.OtherTenantId, EmployeeCode = $"B-{Guid.NewGuid():N}"[..10], FirstName = "B", LastName = "Employee", DateOfJoining = new(2026, 1, 1) };
            var shift = new Shift { Id = Guid.NewGuid(), TenantId = fixture.OtherTenantId, ShiftCode = $"B-{Guid.NewGuid():N}"[..10], ShiftName = "B", IsDefault = true, EffectiveFrom = new(2026, 1, 1), StartTime = new(9, 0), EndTime = new(18, 0), PlannedDurationMinutes = 540, FullDayWorkMinutes = 480, MinimumWorkMinutes = 480 };
            other.Employees.Add(employee); other.Shifts.Add(shift);
            other.EmployeeEmploymentHistory.Add(new EmployeeEmploymentHistory { Id = Guid.NewGuid(), TenantId = fixture.OtherTenantId, EmployeeId = employee.Id, EffectiveFrom = new(2026, 1, 1), EmploymentStatus = EmployeeStatus.Active });
            await other.SaveChangesAsync();
            await using var a = fixture.CreateContext(new TestTenantContext(fixture.TenantId));
            var aService = new AttendanceFoundationService(a, fixture.EmployeeTenant, new EffectiveEmploymentResolver(a, fixture.EmployeeTenant));
            var aRows = await aService.GetRosterAsync(new RosterQuery { FromDate = new(2026, 10, 1), ToDate = new(2026, 10, 1), PageSize = 100 });
            Assert.DoesNotContain(aRows.Value!.Items, x => x.EmployeeCode == employee.EmployeeCode || x.EffectiveShiftCode == shift.ShiftCode);
            await using var b = fixture.CreateContext(new TestTenantContext(fixture.OtherTenantId));
            var bService = new AttendanceFoundationService(b, new TestTenantContext(fixture.OtherTenantId), new EffectiveEmploymentResolver(b, new TestTenantContext(fixture.OtherTenantId)));
            var bRows = await bService.GetRosterAsync(new RosterQuery { FromDate = new(2026, 10, 1), ToDate = new(2026, 10, 1), EmployeeId = employee.Id });
            var row = Assert.Single(bRows.Value!.Items); Assert.Equal(employee.EmployeeCode, row.EmployeeCode); Assert.Equal(shift.ShiftCode, row.EffectiveShiftCode);
        }
        finally
        {
            await using var cleanup = fixture.CreateContext(new TestTenantContext(fixture.OtherTenantId));
            await cleanup.Shifts.Where(x => x.TenantId == fixture.OtherTenantId).ExecuteDeleteAsync();
            await cleanup.EmployeeEmploymentHistory.Where(x => x.TenantId == fixture.OtherTenantId).ExecuteDeleteAsync();
            await cleanup.Employees.Where(x => x.TenantId == fixture.OtherTenantId).ExecuteDeleteAsync();
            await fixture.CleanupAttendanceAsync(); await fixture.CleanupAsync();
        }
    }

    [Fact]
    public async Task Real_mysql_roster_query_calendar_and_explicit_sources_are_effective()
    {
        var connection = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) throw SkipException.ForSkip("MySQL Attendance roster-query tests not executed: connection is absent.");
        var fixture = new MySqlLeaveLifecycleIntegrationTests.Fixture(connection);
        try
        {
            await fixture.SeedAsync();
            await using var setup = fixture.CreateContext(new TestTenantContext(fixture.TenantId));
            var holiday = new DateOnly(2026, 8, 15); var weekly = new DateOnly(2026, 8, 16);
            setup.Holidays.Add(new Holiday { Id = Guid.NewGuid(), TenantId = fixture.TenantId, Name = "Query Holiday", Date = holiday, IsActive = true });
            var off = new WeeklyOffConfiguration { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EffectiveFrom = new(2026, 1, 1), EffectiveTo = new(2026, 12, 31), IsActive = true }; off.Days.Add(new WeeklyOffDay { Id = Guid.NewGuid(), TenantId = fixture.TenantId, WeeklyOffConfigurationId = off.Id, DayOfWeek = DayOfWeek.Sunday }); setup.WeeklyOffConfigurations.Add(off); await setup.SaveChangesAsync();
            await using var db = fixture.CreateContext();
            var shift = new Shift { Id = Guid.NewGuid(), TenantId = fixture.TenantId, ShiftCode = $"Q-{Guid.NewGuid():N}"[..10], ShiftName = "Query", EffectiveFrom = new(2026, 1, 1), StartTime = new(9, 0), EndTime = new(18, 0), PlannedDurationMinutes = 540, FullDayWorkMinutes = 480, MinimumWorkMinutes = 480 };
            db.Shifts.Add(shift); db.EmployeeRosterDays.AddRange(new EmployeeRosterDay { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, RosterDate = new(2026, 8, 17), ShiftId = shift.Id, DayType = RosterDayType.Shift, AssignmentSource = RosterAssignmentSource.Manual, IsOverride = true }, new EmployeeRosterDay { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, RosterDate = new(2026, 8, 18), ShiftId = shift.Id, DayType = RosterDayType.Shift, AssignmentSource = RosterAssignmentSource.Upload, IsOverride = true }); await db.SaveChangesAsync();
            var employment = new EffectiveEmploymentResolver(db, fixture.EmployeeTenant);
            var service = new AttendanceFoundationService(db, fixture.EmployeeTenant, employment, new WorkingDayCalendarResolver(db, fixture.EmployeeTenant, employment));
            var rows = (await service.GetRosterAsync(new RosterQuery { FromDate = holiday, ToDate = new(2026, 8, 18), EmployeeId = fixture.EmployeeId, PageSize = 20 })).Value!.Items;
            Assert.Equal(RosterDayType.Holiday, rows.Single(x => x.RosterDate == holiday).DayType); Assert.Equal(RosterDayType.WeeklyOff, rows.Single(x => x.RosterDate == weekly).DayType); Assert.Equal(RosterAssignmentSource.Manual, rows.Single(x => x.RosterDate == new DateOnly(2026, 8, 17)).AssignmentSource); Assert.Equal(RosterAssignmentSource.Upload, rows.Single(x => x.RosterDate == new DateOnly(2026, 8, 18)).AssignmentSource);
        }
        finally { await fixture.CleanupAttendanceAsync(); await fixture.CleanupAsync(); }
    }

    [Fact]
    public async Task Real_mysql_roster_query_auto_sources_include_applicability_pattern_and_default()
    {
        await WithFixture(async fixture =>
        {
            await using var db = fixture.CreateContext();
            var defaultShift = Shift(fixture.TenantId, "DEF"); defaultShift.IsDefault = true;
            var applicable = Shift(fixture.TenantId, "APP");
            var patterned = Shift(fixture.TenantId, "PAT");
            var pattern = Pattern(fixture.TenantId, patterned.Id, "PATTERN");
            db.Shifts.AddRange(defaultShift, applicable, patterned); db.ShiftPatterns.Add(pattern);
            db.ShiftApplicabilityRules.Add(new ShiftApplicabilityRule { Id = Guid.NewGuid(), TenantId = fixture.TenantId, RuleName = "Applicability", EmployeeId = fixture.EmployeeId, ShiftId = applicable.Id, Priority = 10, EffectiveFrom = new(2026, 10, 2), EffectiveTo = new(2026, 10, 2) });
            db.ShiftApplicabilityRules.Add(new ShiftApplicabilityRule { Id = Guid.NewGuid(), TenantId = fixture.TenantId, RuleName = "Pattern", EmployeeId = fixture.EmployeeId, ShiftPatternId = pattern.Id, Priority = 10, EffectiveFrom = new(2026, 10, 3), EffectiveTo = new(2026, 10, 3) });
            await db.SaveChangesAsync();
            var service = Service(db, fixture);
            var rows = (await service.GetRosterAsync(new RosterQuery { FromDate = new(2026, 10, 1), ToDate = new(2026, 10, 3), EmployeeId = fixture.EmployeeId, PageSize = 10 })).Value!.Items;
            Assert.Equal(defaultShift.Id, rows.Single(x => x.RosterDate == new DateOnly(2026, 10, 1)).EffectiveShiftId);
            Assert.Equal(applicable.Id, rows.Single(x => x.RosterDate == new DateOnly(2026, 10, 2)).EffectiveShiftId);
            Assert.Equal(patterned.Id, rows.Single(x => x.RosterDate == new DateOnly(2026, 10, 3)).EffectiveShiftId);
            Assert.Equal(RosterAssignmentSource.Auto, rows.Single(x => x.RosterDate == new DateOnly(2026, 10, 3)).AssignmentSource);
        });
    }

    [Fact]
    public async Task Real_mysql_roster_query_calendar_types_are_tenant_scoped()
    {
        await WithFixture(async fixture =>
        {
            await using var db = fixture.CreateContext();
            db.Holidays.Add(new Holiday { Id = Guid.NewGuid(), TenantId = fixture.TenantId, Name = "MySQL Holiday", Date = new(2026, 8, 15), IsActive = true });
            var weekly = new WeeklyOffConfiguration { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EffectiveFrom = new(2026, 1, 1), EffectiveTo = new(2026, 12, 31), IsActive = true };
            weekly.Days.Add(new WeeklyOffDay { Id = Guid.NewGuid(), TenantId = fixture.TenantId, WeeklyOffConfigurationId = weekly.Id, DayOfWeek = DayOfWeek.Sunday }); db.WeeklyOffConfigurations.Add(weekly);
            await db.SaveChangesAsync();
            var rows = (await Service(db, fixture).GetRosterAsync(new RosterQuery { FromDate = new(2026, 8, 15), ToDate = new(2026, 8, 16), EmployeeId = fixture.EmployeeId, PageSize = 10 })).Value!.Items;
            Assert.Equal(RosterDayType.Holiday, rows.Single(x => x.RosterDate == new DateOnly(2026, 8, 15)).DayType);
            Assert.Equal(RosterDayType.WeeklyOff, rows.Single(x => x.RosterDate == new DateOnly(2026, 8, 16)).DayType);
        });
    }

    [Fact]
    public async Task Real_mysql_roster_query_explicit_sources_and_calendar_overrides_are_effective()
    {
        await WithFixture(async fixture =>
        {
            await using var db = fixture.CreateContext();
            var shift = Shift(fixture.TenantId, "EXPLICIT"); db.Shifts.Add(shift);
            db.Holidays.Add(new Holiday { Id = Guid.NewGuid(), TenantId = fixture.TenantId, Name = "Override Holiday", Date = new(2026, 8, 15), IsActive = true });
            var weekly = new WeeklyOffConfiguration { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EffectiveFrom = new(2026, 1, 1), EffectiveTo = new(2026, 12, 31), IsActive = true };
            weekly.Days.Add(new WeeklyOffDay { Id = Guid.NewGuid(), TenantId = fixture.TenantId, WeeklyOffConfigurationId = weekly.Id, DayOfWeek = DayOfWeek.Sunday }); db.WeeklyOffConfigurations.Add(weekly);
            db.EmployeeRosterDays.AddRange(
                new EmployeeRosterDay { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, RosterDate = new(2026, 8, 15), ShiftId = shift.Id, DayType = RosterDayType.Shift, AssignmentSource = RosterAssignmentSource.Manual, IsOverride = true, IsCalendarOverride = true, OriginalCalendarDayType = RosterCalendarDayType.Holiday },
                new EmployeeRosterDay { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, RosterDate = new(2026, 8, 16), ShiftId = shift.Id, DayType = RosterDayType.Shift, AssignmentSource = RosterAssignmentSource.Upload, IsOverride = true, IsCalendarOverride = true, OriginalCalendarDayType = RosterCalendarDayType.WeeklyOff },
                new EmployeeRosterDay { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, RosterDate = new(2026, 8, 17), ShiftId = null, DayType = RosterDayType.WeeklyOff, AssignmentSource = RosterAssignmentSource.Manual, IsOverride = true });
            await db.SaveChangesAsync();
            var rows = (await Service(db, fixture).GetRosterAsync(new RosterQuery { FromDate = new(2026, 8, 15), ToDate = new(2026, 8, 17), EmployeeId = fixture.EmployeeId, PageSize = 10 })).Value!.Items;
            var holiday = rows.Single(x => x.RosterDate == new DateOnly(2026, 8, 15)); Assert.Equal(RosterAssignmentSource.Manual, holiday.AssignmentSource); Assert.True(holiday.IsCalendarOverride); Assert.Equal(RosterCalendarDayType.Holiday, holiday.UnderlyingCalendarDayType);
            var weeklyRow = rows.Single(x => x.RosterDate == new DateOnly(2026, 8, 16)); Assert.Equal(RosterAssignmentSource.Upload, weeklyRow.AssignmentSource); Assert.Equal(RosterCalendarDayType.WeeklyOff, weeklyRow.UnderlyingCalendarDayType);
            Assert.Equal(RosterDayType.WeeklyOff, rows.Single(x => x.RosterDate == new DateOnly(2026, 8, 17)).DayType);
        });
    }

    [Fact]
    public async Task Real_mysql_roster_query_filters_and_pagination_apply_to_effective_rows()
    {
        await WithFixture(async fixture =>
        {
            await using var db = fixture.CreateContext();
            var morning = Shift(fixture.TenantId, "FILTER-M"); var evening = Shift(fixture.TenantId, "FILTER-E"); db.Shifts.AddRange(morning, evening);
            var employee2 = new Employee { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeCode = $"F{Guid.NewGuid():N}"[..8], FirstName = "Filter", LastName = "Two", DateOfJoining = new(2026, 1, 1) }; db.Employees.Add(employee2); db.EmployeeEmploymentHistory.Add(new EmployeeEmploymentHistory { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = employee2.Id, EffectiveFrom = new(2026, 1, 1), EmploymentStatus = EmployeeStatus.Active });
            db.EmployeeRosterDays.Add(new EmployeeRosterDay { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, RosterDate = new(2026, 9, 1), ShiftId = morning.Id, DayType = RosterDayType.Shift, AssignmentSource = RosterAssignmentSource.Manual, IsOverride = true });
            db.EmployeeRosterDays.Add(new EmployeeRosterDay { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = employee2.Id, RosterDate = new(2026, 9, 1), ShiftId = evening.Id, DayType = RosterDayType.Shift, AssignmentSource = RosterAssignmentSource.Upload, IsOverride = true }); await db.SaveChangesAsync();
            var service = Service(db, fixture);
            var employeeRows = (await service.GetRosterAsync(new RosterQuery { FromDate = new(2026, 9, 1), ToDate = new(2026, 9, 1), EmployeeId = fixture.EmployeeId, PageSize = 10 })).Value!.Items; Assert.All(employeeRows, x => Assert.Equal(fixture.EmployeeId, x.EmployeeId));
            var shiftRows = (await service.GetRosterAsync(new RosterQuery { FromDate = new(2026, 9, 1), ToDate = new(2026, 9, 1), ShiftId = morning.Id, PageSize = 10 })).Value!.Items; Assert.All(shiftRows, x => Assert.Equal(morning.Id, x.EffectiveShiftId));
            var manualRows = (await service.GetRosterAsync(new RosterQuery { FromDate = new(2026, 9, 1), ToDate = new(2026, 9, 1), AssignmentSource = RosterAssignmentSource.Manual, PageSize = 10 })).Value!.Items; Assert.All(manualRows, x => Assert.Equal(RosterAssignmentSource.Manual, x.AssignmentSource));
            var page1 = await service.GetRosterAsync(new RosterQuery { FromDate = new(2026, 9, 1), ToDate = new(2026, 9, 3), PageSize = 2, Page = 1 }); var page2 = await service.GetRosterAsync(new RosterQuery { FromDate = new(2026, 9, 1), ToDate = new(2026, 9, 3), PageSize = 2, Page = 2 }); Assert.Equal(2, page1.Value!.Items.Count); Assert.Equal(page1.Value.TotalCount, page2.Value!.TotalCount); Assert.Empty(page1.Value.Items.Select(x => x.EmployeeId + "|" + x.RosterDate).Intersect(page2.Value.Items.Select(x => x.EmployeeId + "|" + x.RosterDate)));
        });
    }

    [Fact]
    public async Task Real_mysql_roster_query_full_tenant_isolation_protects_effective_sources()
    {
        await WithFixture(async fixture =>
        {
            await using var b = fixture.CreateContext(new TestTenantContext(fixture.OtherTenantId));
            var employee = new Employee { Id = Guid.NewGuid(), TenantId = fixture.OtherTenantId, EmployeeCode = $"B{Guid.NewGuid():N}"[..8], FirstName = "B", LastName = "Employee", DateOfJoining = new(2026, 1, 1) }; var shift = Shift(fixture.OtherTenantId, "B_SHIFT"); b.Employees.Add(employee); b.Shifts.Add(shift); b.EmployeeEmploymentHistory.Add(new EmployeeEmploymentHistory { Id = Guid.NewGuid(), TenantId = fixture.OtherTenantId, EmployeeId = employee.Id, EffectiveFrom = new(2026, 1, 1), EmploymentStatus = EmployeeStatus.Active }); b.Holidays.Add(new Holiday { Id = Guid.NewGuid(), TenantId = fixture.OtherTenantId, Name = "B Holiday", Date = new(2026, 8, 15), IsActive = true }); b.EmployeeRosterDays.Add(new EmployeeRosterDay { Id = Guid.NewGuid(), TenantId = fixture.OtherTenantId, EmployeeId = employee.Id, RosterDate = new(2026, 8, 17), ShiftId = shift.Id, DayType = RosterDayType.Shift, AssignmentSource = RosterAssignmentSource.Upload, IsOverride = true }); await b.SaveChangesAsync();
            await using var a = fixture.CreateContext(new TestTenantContext(fixture.TenantId)); var aRows = (await Service(a, fixture).GetRosterAsync(new RosterQuery { FromDate = new(2026, 8, 15), ToDate = new(2026, 8, 17), PageSize = 100 })).Value!.Items; Assert.DoesNotContain(aRows, x => x.EmployeeCode == employee.EmployeeCode || x.EffectiveShiftCode == shift.ShiftCode);
            await using var bRead = fixture.CreateContext(new TestTenantContext(fixture.OtherTenantId)); var bRows = (await Service(bRead, fixture, fixture.OtherTenantId).GetRosterAsync(new RosterQuery { FromDate = new(2026, 8, 15), ToDate = new(2026, 8, 17), EmployeeId = employee.Id, PageSize = 100 })).Value!.Items; Assert.Contains(bRows, x => x.EmployeeCode == employee.EmployeeCode); Assert.Contains(bRows, x => x.RosterDate == new DateOnly(2026, 8, 15) && x.DayType == RosterDayType.Holiday); Assert.Contains(bRows, x => x.RosterDate == new DateOnly(2026, 8, 17) && x.AssignmentSource == RosterAssignmentSource.Upload);
        });
    }

    private static Shift Shift(Guid tenantId, string code) => new() { Id = Guid.NewGuid(), TenantId = tenantId, ShiftCode = $"{code}-{Guid.NewGuid():N}"[..Math.Min(20, code.Length + 9)], ShiftName = code, EffectiveFrom = new(2026, 1, 1), StartTime = new(9, 0), EndTime = new(18, 0), PlannedDurationMinutes = 540, FullDayWorkMinutes = 480, MinimumWorkMinutes = 480, IsActive = true };
    private static ShiftPattern Pattern(Guid tenantId, Guid shiftId, string code) { var p = new ShiftPattern { Id = Guid.NewGuid(), TenantId = tenantId, Code = $"{code}-{Guid.NewGuid():N}"[..Math.Min(20, code.Length + 9)], Name = code, CycleLengthDays = 1, EffectiveFrom = new(2026, 1, 1), IsActive = true }; p.Days.Add(new ShiftPatternDay { Id = Guid.NewGuid(), TenantId = tenantId, ShiftPatternId = p.Id, SequenceDay = 1, ShiftId = shiftId, DayType = ShiftPatternDayType.Shift }); return p; }
    private static AttendanceFoundationService Service(HRMS.Infrastructure.Persistence.HrmsDbContext db, MySqlLeaveLifecycleIntegrationTests.Fixture fixture, Guid? tenantId = null) { var tenant = tenantId.HasValue ? new TestTenantContext(tenantId.Value) : fixture.EmployeeTenant; var employment = new EffectiveEmploymentResolver(db, tenant); return new AttendanceFoundationService(db, tenant, employment, new WorkingDayCalendarResolver(db, tenant, employment)); }
    private static async Task WithFixture(Func<MySqlLeaveLifecycleIntegrationTests.Fixture, Task> action)
    { var connection = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION"); if (string.IsNullOrWhiteSpace(connection)) throw SkipException.ForSkip("MySQL Attendance roster-query tests not executed: connection is absent."); var fixture = new MySqlLeaveLifecycleIntegrationTests.Fixture(connection); try { await fixture.SeedAsync(); await action(fixture); } finally { await using var cleanup = fixture.CreateContext(new TestTenantContext(fixture.OtherTenantId)); await cleanup.EmployeeRosterDays.Where(x => x.TenantId == fixture.OtherTenantId).ExecuteDeleteAsync(); await cleanup.EmployeeEmploymentHistory.Where(x => x.TenantId == fixture.OtherTenantId).ExecuteDeleteAsync(); await cleanup.Employees.Where(x => x.TenantId == fixture.OtherTenantId).ExecuteDeleteAsync(); await cleanup.Shifts.Where(x => x.TenantId == fixture.OtherTenantId).ExecuteDeleteAsync(); await cleanup.Holidays.Where(x => x.TenantId == fixture.OtherTenantId).ExecuteDeleteAsync(); await fixture.CleanupAttendanceAsync(); await fixture.CleanupAsync(); } }
}
