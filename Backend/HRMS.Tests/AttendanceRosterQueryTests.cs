using System.Diagnostics;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Attendance;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

[CollectionDefinition("Attendance roster query", DisableParallelization = true)]
public sealed class AttendanceRosterQueryCollectionDefinition;

[Collection("Attendance roster query")]
public sealed class AttendanceRosterQueryTests
{
    [Fact]
    public async Task Effective_query_includes_default_and_holiday_rows_without_persisting_roster()
    {
        using var fixture = await AttendanceTestFixture.CreateAsync();
        await fixture.AddShiftAsync("DEFAULT", isDefault: true);
        await fixture.AddHolidayAsync(new DateOnly(2026, 8, 15));

        var result = await fixture.CalendarService.GetRosterAsync(new RosterQuery { FromDate = new(2026, 8, 15), ToDate = new(2026, 8, 16), PageSize = 10 });

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(2, result.Value!.TotalCount);
        Assert.Contains(result.Value.Items, x => x.DayType == RosterDayType.Holiday && !x.HasExplicitRoster);
        Assert.Contains(result.Value.Items, x => x.EffectiveShiftCode == "DEFAULT" && x.AssignmentSource == RosterAssignmentSource.System);
    }

    [Fact]
    public async Task Effective_query_includes_manual_override_and_filters_it()
    {
        using var fixture = await AttendanceTestFixture.CreateAsync();
        var shift = await fixture.AddShiftAsync("MANUAL");
        await fixture.CalendarService.AssignRosterAsync(new() { EmployeeIds = [fixture.EmployeeId], FromDate = new(2026, 10, 1), ToDate = new(2026, 10, 1), ShiftId = shift.Id, DayType = RosterDayType.Shift });

        var result = await fixture.CalendarService.GetRosterAsync(new RosterQuery { FromDate = new(2026, 10, 1), ToDate = new(2026, 10, 1), AssignmentSource = RosterAssignmentSource.Manual });

        Assert.True(result.Succeeded, result.Message);
        var row = Assert.Single(result.Value!.Items);
        Assert.Equal("MANUAL", row.EffectiveShiftCode);
        Assert.True(row.HasExplicitRoster);
    }

    [Fact]
    public async Task Effective_query_requires_bounded_date_range_and_paginates()
    {
        using var fixture = await AttendanceTestFixture.CreateAsync();
        await fixture.AddShiftAsync("DEFAULT", isDefault: true);

        var result = await fixture.CalendarService.GetRosterAsync(new RosterQuery { Page = 1, PageSize = 1, FromDate = new(2026, 10, 1), ToDate = new(2026, 10, 3) });
        var unbounded = await fixture.CalendarService.GetRosterAsync(new RosterQuery());

        Assert.True(result.Succeeded, result.Message);
        Assert.Single(result.Value!.Items);
        Assert.Equal(3, result.Value.TotalCount);
        Assert.False(unbounded.Succeeded);
    }

    [Fact]
    public async Task Effective_query_resolves_applicability_and_pattern_rows()
    {
        using var fixture = await AttendanceTestFixture.CreateAsync();
        var morning = await fixture.AddShiftAsync("MORNING");
        var evening = await fixture.AddShiftAsync("EVENING");
        var pattern = new ShiftPattern { Id = Guid.NewGuid(), TenantId = fixture.TenantId, Code = "PAT-Q", Name = "PAT-Q", CycleLengthDays = 2, EffectiveFrom = new(2026, 10, 1) };
        pattern.Days.Add(new ShiftPatternDay { Id = Guid.NewGuid(), TenantId = fixture.TenantId, SequenceDay = 1, ShiftId = morning.Id, DayType = ShiftPatternDayType.Shift });
        pattern.Days.Add(new ShiftPatternDay { Id = Guid.NewGuid(), TenantId = fixture.TenantId, SequenceDay = 2, ShiftId = evening.Id, DayType = ShiftPatternDayType.Shift });
        fixture.Context.ShiftPatterns.Add(pattern);
        await fixture.AddRuleAsync(null, departmentId: fixture.DepartmentId, patternId: pattern.Id, effectiveFrom: new(2026, 10, 1));
        await fixture.Context.SaveChangesAsync();

        var result = await fixture.CalendarService.GetRosterAsync(new() { FromDate = new(2026, 10, 1), ToDate = new(2026, 10, 2), PageSize = 10 });

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(["MORNING", "EVENING"], result.Value!.Items.OrderBy(x => x.RosterDate).Select(x => x.EffectiveShiftCode));
        Assert.All(result.Value.Items, x => Assert.False(x.HasExplicitRoster));
    }

    [Fact]
    public async Task Effective_query_preserves_holiday_and_weekly_off_without_default_leakage()
    {
        using var fixture = await AttendanceTestFixture.CreateAsync();
        await fixture.AddShiftAsync("DEFAULT", isDefault: true);
        await fixture.AddHolidayAsync(new(2026, 8, 15));
        await fixture.AddWeeklyOffAsync(DayOfWeek.Sunday);

        var result = await fixture.CalendarService.GetRosterAsync(new() { FromDate = new(2026, 8, 15), ToDate = new(2026, 8, 16), PageSize = 10 });

        Assert.True(result.Succeeded, result.Message);
        var holiday = Assert.Single(result.Value!.Items, x => x.RosterDate == new DateOnly(2026, 8, 15));
        Assert.Equal(RosterDayType.Holiday, holiday.DayType);
        Assert.Null(holiday.EffectiveShiftId);
        Assert.Equal(RosterDayType.Shift, Assert.Single(result.Value.Items, x => x.RosterDate == new DateOnly(2026, 8, 16)).DayType);
    }

    [Fact]
    public async Task Effective_query_reports_manual_and_upload_sources_as_explicit_rows()
    {
        using var fixture = await AttendanceTestFixture.CreateAsync();
        var manual = await fixture.AddShiftAsync("MANUAL");
        var upload = await fixture.AddShiftAsync("UPLOAD");
        fixture.Context.EmployeeRosterDays.AddRange(
            new EmployeeRosterDay { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, RosterDate = new(2026, 10, 1), ShiftId = manual.Id, DayType = RosterDayType.Shift, AssignmentSource = RosterAssignmentSource.Manual, IsOverride = true },
            new EmployeeRosterDay { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, RosterDate = new(2026, 10, 2), ShiftId = upload.Id, DayType = RosterDayType.Shift, AssignmentSource = RosterAssignmentSource.Upload, IsOverride = true });
        await fixture.Context.SaveChangesAsync();

        var result = await fixture.CalendarService.GetRosterAsync(new() { FromDate = new(2026, 10, 1), ToDate = new(2026, 10, 2), PageSize = 10 });

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(RosterAssignmentSource.Manual, result.Value!.Items.Single(x => x.RosterDate == new DateOnly(2026, 10, 1)).AssignmentSource);
        Assert.Equal(RosterAssignmentSource.Upload, result.Value.Items.Single(x => x.RosterDate == new DateOnly(2026, 10, 2)).AssignmentSource);
        Assert.All(result.Value.Items, x => Assert.True(x.HasExplicitRoster));
    }

    [Fact]
    public async Task Effective_query_reports_weekly_off_and_weekly_off_to_shift_override()
    {
        using var fixture = await AttendanceTestFixture.CreateAsync();
        var shift = await fixture.AddShiftAsync("WEEKLY-MORNING");
        await fixture.AddWeeklyOffAsync(DayOfWeek.Sunday);
        var date = new DateOnly(2026, 8, 16);

        var baseline = await fixture.CalendarService.GetRosterAsync(new() { FromDate = date, ToDate = date });
        var baselineRow = Assert.Single(baseline.Value!.Items);
        Assert.Equal(RosterDayType.WeeklyOff, baselineRow.DayType);
        Assert.Null(baselineRow.EffectiveShiftId);
        Assert.False(baselineRow.HasExplicitRoster);

        fixture.Context.EmployeeRosterDays.Add(new EmployeeRosterDay { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, RosterDate = date, ShiftId = shift.Id, DayType = RosterDayType.Shift, AssignmentSource = RosterAssignmentSource.Manual, IsOverride = true, IsCalendarOverride = true, OriginalCalendarDayType = RosterCalendarDayType.WeeklyOff });
        await fixture.Context.SaveChangesAsync();
        var overridden = await fixture.CalendarService.GetRosterAsync(new() { FromDate = date, ToDate = date });
        var row = Assert.Single(overridden.Value!.Items);
        Assert.Equal(RosterDayType.Shift, row.DayType);
        Assert.Equal("WEEKLY-MORNING", row.EffectiveShiftCode);
        Assert.Equal(RosterAssignmentSource.Manual, row.AssignmentSource);
        Assert.True(row.HasExplicitRoster);
        Assert.True(row.IsCalendarOverride);
        Assert.Equal(RosterCalendarDayType.WeeklyOff, row.UnderlyingCalendarDayType);
    }

    [Fact]
    public async Task Effective_query_reports_working_shift_to_weekly_off_override()
    {
        using var fixture = await AttendanceTestFixture.CreateAsync();
        var shift = await fixture.AddShiftAsync("WORKING");
        fixture.Context.EmployeeRosterDays.Add(new EmployeeRosterDay { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, RosterDate = new(2026, 10, 1), ShiftId = shift.Id, DayType = RosterDayType.WeeklyOff, AssignmentSource = RosterAssignmentSource.Manual, IsOverride = true, IsCalendarOverride = true, OriginalCalendarDayType = RosterCalendarDayType.WorkingDay });
        await fixture.Context.SaveChangesAsync();

        var result = await fixture.CalendarService.GetRosterAsync(new() { FromDate = new(2026, 10, 1), ToDate = new(2026, 10, 1) });
        var row = Assert.Single(result.Value!.Items);
        Assert.Equal(RosterDayType.WeeklyOff, row.DayType);
        Assert.Null(row.EffectiveShiftId);
        Assert.True(row.HasExplicitRoster);
        Assert.Equal(RosterAssignmentSource.Manual, row.AssignmentSource);
    }

    [Fact]
    public async Task Effective_query_filters_employee_day_type_and_source()
    {
        using var fixture = await AttendanceTestFixture.CreateAsync();
        var second = Guid.NewGuid();
        await fixture.AddEmployeeAsync(second, "E002", fixture.DepartmentId, fixture.WorkLocationId, fixture.EmployeeTypeId, fixture.GradeId, fixture.DesignationId, fixture.CostCenterId);
        var shift = await fixture.AddShiftAsync("FILTER-MORNING");
        fixture.Context.EmployeeRosterDays.AddRange(
            new EmployeeRosterDay { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, RosterDate = new(2026, 10, 1), ShiftId = shift.Id, DayType = RosterDayType.Shift, AssignmentSource = RosterAssignmentSource.Manual, IsOverride = true },
            new EmployeeRosterDay { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = second, RosterDate = new(2026, 10, 1), ShiftId = shift.Id, DayType = RosterDayType.Shift, AssignmentSource = RosterAssignmentSource.Upload, IsOverride = true });
        await fixture.Context.SaveChangesAsync();

        var employee = await fixture.CalendarService.GetRosterAsync(new() { FromDate = new(2026, 10, 1), ToDate = new(2026, 10, 1), EmployeeId = fixture.EmployeeId });
        Assert.All(employee.Value!.Items, x => Assert.Equal(fixture.EmployeeId, x.EmployeeId));
        var manual = await fixture.CalendarService.GetRosterAsync(new() { FromDate = new(2026, 10, 1), ToDate = new(2026, 10, 1), AssignmentSource = RosterAssignmentSource.Manual });
        Assert.Single(manual.Value!.Items);
        Assert.Equal(fixture.EmployeeId, manual.Value.Items[0].EmployeeId);
        var shiftRows = await fixture.CalendarService.GetRosterAsync(new() { FromDate = new(2026, 10, 1), ToDate = new(2026, 10, 1), ShiftId = shift.Id });
        Assert.Equal(2, shiftRows.Value!.TotalCount);
    }

    [Fact]
    public async Task Effective_query_performance_smoke_completes_bounded_scopes()
    {
        using var fixture = await AttendanceTestFixture.CreateAsync();
        await fixture.AddShiftAsync("PERF-DEFAULT", isDefault: true);
        for (var i = 2; i <= 5; i++) await fixture.AddEmployeeAsync(Guid.NewGuid(), $"E{i:000}", fixture.DepartmentId, fixture.WorkLocationId, fixture.EmployeeTypeId, fixture.GradeId, fixture.DesignationId, fixture.CostCenterId);

        static async Task<(int Count, long Milliseconds)> Measure(Func<Task<Result<PagedResult<RosterGridRowDto>>>> query)
        {
            var stopwatch = Stopwatch.StartNew();
            var result = await query();
            stopwatch.Stop();
            Assert.True(result.Succeeded, result.Message);
            return (result.Value!.TotalCount, stopwatch.ElapsedMilliseconds);
        }

        var oneByOne = await Measure(() => fixture.CalendarService.GetRosterAsync(new() { FromDate = new(2026, 10, 1), ToDate = new(2026, 10, 1), PageSize = 100 }));
        var oneBySeven = await Measure(() => fixture.CalendarService.GetRosterAsync(new() { FromDate = new(2026, 10, 1), ToDate = new(2026, 10, 7), PageSize = 100 }));
        var fiveBySeven = await Measure(() => fixture.CalendarService.GetRosterAsync(new() { FromDate = new(2026, 10, 1), ToDate = new(2026, 10, 7), PageSize = 100 }));
        Console.WriteLine($"Attendance roster performance smoke: 1x1={oneByOne.Milliseconds}ms/{oneByOne.Count} rows; 1x7={oneBySeven.Milliseconds}ms/{oneBySeven.Count} rows; 5x7={fiveBySeven.Milliseconds}ms/{fiveBySeven.Count} rows");
        Assert.True(oneByOne.Count >= 1);
        Assert.True(oneBySeven.Count >= oneByOne.Count);
        Assert.True(fiveBySeven.Count >= oneBySeven.Count);
    }

    [Fact]
    public async Task Effective_query_day_type_and_source_filters_return_only_matching_rows()
    {
        using var fixture = await AttendanceTestFixture.CreateAsync();
        var shift = await fixture.AddShiftAsync("FILTER-SHIFT");
        await fixture.AddHolidayAsync(new(2026, 8, 15));
        await fixture.AddWeeklyOffAsync(DayOfWeek.Sunday);
        fixture.Context.EmployeeRosterDays.AddRange(
            new EmployeeRosterDay { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, RosterDate = new(2026, 8, 14), ShiftId = shift.Id, DayType = RosterDayType.Shift, AssignmentSource = RosterAssignmentSource.Manual, IsOverride = true },
            new EmployeeRosterDay { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, RosterDate = new(2026, 8, 16), ShiftId = null, DayType = RosterDayType.WeeklyOff, AssignmentSource = RosterAssignmentSource.Upload, IsOverride = true });
        await fixture.Context.SaveChangesAsync();

        var holiday = await fixture.CalendarService.GetRosterAsync(new() { FromDate = new(2026, 8, 15), ToDate = new(2026, 8, 15), DayType = RosterDayType.Holiday });
        var weeklyOff = await fixture.CalendarService.GetRosterAsync(new() { FromDate = new(2026, 8, 16), ToDate = new(2026, 8, 16), DayType = RosterDayType.WeeklyOff });
        var shiftRows = await fixture.CalendarService.GetRosterAsync(new() { FromDate = new(2026, 8, 14), ToDate = new(2026, 8, 14), DayType = RosterDayType.Shift });
        var uploadRows = await fixture.CalendarService.GetRosterAsync(new() { FromDate = new(2026, 8, 16), ToDate = new(2026, 8, 16), AssignmentSource = RosterAssignmentSource.Upload });

        Assert.All(holiday.Value!.Items, x => Assert.Equal(RosterDayType.Holiday, x.DayType));
        Assert.All(weeklyOff.Value!.Items, x => Assert.Equal(RosterDayType.WeeklyOff, x.DayType));
        Assert.All(shiftRows.Value!.Items, x => Assert.Equal(RosterDayType.Shift, x.DayType));
        Assert.All(uploadRows.Value!.Items, x => Assert.Equal(RosterAssignmentSource.Upload, x.AssignmentSource));
    }

    [Fact]
    public async Task Effective_query_is_tenant_isolated()
    {
        using var fixture = await AttendanceTestFixture.CreateAsync();
        var shift = await fixture.AddShiftAsync("TENANT-SHIFT", isDefault: true);
        var otherTenant = Guid.NewGuid();
        fixture.Context.Tenants.Add(new Tenant { Id = otherTenant, TenantCode = $"T-{otherTenant:N}"[..20], Host = $"{otherTenant:N}.test", ShardKey = otherTenant.ToString("N"), TenantName = "Other" });
        fixture.Context.Employees.Add(new Employee { Id = Guid.NewGuid(), TenantId = otherTenant, EmployeeCode = "OTHER", FirstName = "Other", LastName = "Tenant", DateOfJoining = new(2026, 1, 1) });
        fixture.Context.Shifts.Add(new Shift { Id = Guid.NewGuid(), TenantId = otherTenant, ShiftCode = "OTHER-SHIFT", ShiftName = "Other", EffectiveFrom = new(2026, 1, 1), StartTime = new(9, 0), EndTime = new(18, 0), PlannedDurationMinutes = 540, FullDayWorkMinutes = 480, MinimumWorkMinutes = 480 });
        await fixture.Context.SaveChangesAsync();

        var result = await fixture.CalendarService.GetRosterAsync(new() { FromDate = new(2026, 10, 1), ToDate = new(2026, 10, 1), PageSize = 100 });
        Assert.Single(result.Value!.Items);
        Assert.Equal(fixture.EmployeeId, result.Value.Items[0].EmployeeId);
        Assert.Equal(shift.Id, result.Value.Items[0].EffectiveShiftId);
        Assert.DoesNotContain(result.Value.Items, x => x.EmployeeCode == "OTHER");
    }

    [Fact]
    public async Task Effective_query_filters_default_and_applicability_sources()
    {
        using var fixture = await AttendanceTestFixture.CreateAsync();
        var defaultShift = await fixture.AddShiftAsync("FILTER-DEFAULT", isDefault: true);
        var applicableShift = await fixture.AddShiftAsync("FILTER-APPLICABLE");
        await fixture.AddRuleAsync(applicableShift.Id, departmentId: fixture.DepartmentId, effectiveFrom: new(2026, 10, 2));

        var defaultRows = await fixture.CalendarService.GetRosterAsync(new() { FromDate = new(2026, 10, 1), ToDate = new(2026, 10, 1), AssignmentSource = RosterAssignmentSource.System });
        var applicabilityRows = await fixture.CalendarService.GetRosterAsync(new() { FromDate = new(2026, 10, 2), ToDate = new(2026, 10, 2), AssignmentSource = RosterAssignmentSource.Auto });

        var defaultRow = Assert.Single(defaultRows.Value!.Items);
        Assert.Equal(defaultShift.Id, defaultRow.EffectiveShiftId);
        Assert.Equal(RosterAssignmentSource.System, defaultRow.AssignmentSource);
        var applicabilityRow = Assert.Single(applicabilityRows.Value!.Items);
        Assert.Equal(applicableShift.Id, applicabilityRow.EffectiveShiftId);
        Assert.Equal(RosterAssignmentSource.Auto, applicabilityRow.AssignmentSource);
    }

    [Fact]
    public async Task Effective_query_isolates_pattern_calendar_manual_and_upload_data_between_tenants()
    {
        using var fixture = await AttendanceTestFixture.CreateAsync();
        var tenantB = Guid.NewGuid(); var employeeB = Guid.NewGuid();
        fixture.Context.Tenants.Add(new Tenant { Id = tenantB, TenantCode = $"T-{tenantB:N}"[..20], Host = $"{tenantB:N}.test", ShardKey = tenantB.ToString("N"), TenantName = "Tenant B" });
        await fixture.Context.SaveChangesAsync();
        await using var tenantBSeedContext = fixture.CreateContext(tenantB, out var tenantBSeed);
        tenantBSeedContext.Employees.Add(new Employee { Id = employeeB, TenantId = tenantB, EmployeeCode = "B_EMP", FirstName = "B", LastName = "Employee", DateOfJoining = new(2026, 1, 1) });
        tenantBSeedContext.EmployeeEmploymentHistory.Add(new EmployeeEmploymentHistory { Id = Guid.NewGuid(), TenantId = tenantB, EmployeeId = employeeB, EffectiveFrom = new(2026, 1, 1), EmploymentStatus = EmployeeStatus.Active });
        await tenantBSeedContext.SaveChangesAsync();
        var aShift = await fixture.AddShiftAsync("A_MORNING", isDefault: true);
        var bShift = new Shift { Id = Guid.NewGuid(), TenantId = tenantB, ShiftCode = "B_EVENING", ShiftName = "B Evening", EffectiveFrom = new(2026, 1, 1), StartTime = new(14, 0), EndTime = new(22, 0), PlannedDurationMinutes = 540, FullDayWorkMinutes = 480, MinimumWorkMinutes = 480 };
        var bManual = new Shift { Id = Guid.NewGuid(), TenantId = tenantB, ShiftCode = "B_MANUAL", ShiftName = "B Manual", EffectiveFrom = new(2026, 1, 1), StartTime = new(10, 0), EndTime = new(18, 0), PlannedDurationMinutes = 540, FullDayWorkMinutes = 480, MinimumWorkMinutes = 480 };
        var bUpload = new Shift { Id = Guid.NewGuid(), TenantId = tenantB, ShiftCode = "B_UPLOAD", ShiftName = "B Upload", EffectiveFrom = new(2026, 1, 1), StartTime = new(8, 0), EndTime = new(16, 0), PlannedDurationMinutes = 540, FullDayWorkMinutes = 480, MinimumWorkMinutes = 480 };
        tenantBSeedContext.Shifts.AddRange(bShift, bManual, bUpload);
        var bPattern = new ShiftPattern { Id = Guid.NewGuid(), TenantId = tenantB, Code = "B_PATTERN", Name = "B Pattern", CycleLengthDays = 1, EffectiveFrom = new(2026, 10, 1) };
        bPattern.Days.Add(new ShiftPatternDay { Id = Guid.NewGuid(), TenantId = tenantB, SequenceDay = 1, ShiftId = bShift.Id, DayType = ShiftPatternDayType.Shift });
        tenantBSeedContext.ShiftPatterns.Add(bPattern);
        tenantBSeedContext.ShiftApplicabilityRules.Add(new ShiftApplicabilityRule { Id = Guid.NewGuid(), TenantId = tenantB, RuleName = "B Pattern Rule", EmployeeId = employeeB, ShiftPatternId = bPattern.Id, Priority = 10, EffectiveFrom = new(2026, 10, 1) });
        tenantBSeedContext.Holidays.Add(new Holiday { Id = Guid.NewGuid(), TenantId = tenantB, Name = "B Holiday", Date = new(2026, 8, 15), IsActive = true });
        var bWeekly = new WeeklyOffConfiguration { Id = Guid.NewGuid(), TenantId = tenantB, EffectiveFrom = new(2026, 1, 1), EffectiveTo = new(2026, 12, 31), IsActive = true };
        bWeekly.Days.Add(new WeeklyOffDay { Id = Guid.NewGuid(), TenantId = tenantB, WeeklyOffConfigurationId = bWeekly.Id, DayOfWeek = DayOfWeek.Sunday }); tenantBSeedContext.WeeklyOffConfigurations.Add(bWeekly);
        tenantBSeedContext.EmployeeRosterDays.AddRange(
            new EmployeeRosterDay { Id = Guid.NewGuid(), TenantId = tenantB, EmployeeId = employeeB, RosterDate = new(2026, 8, 17), ShiftId = bManual.Id, DayType = RosterDayType.Shift, AssignmentSource = RosterAssignmentSource.Manual, IsOverride = true },
            new EmployeeRosterDay { Id = Guid.NewGuid(), TenantId = tenantB, EmployeeId = employeeB, RosterDate = new(2026, 8, 18), ShiftId = bUpload.Id, DayType = RosterDayType.Shift, AssignmentSource = RosterAssignmentSource.Upload, IsOverride = true });
        await tenantBSeedContext.SaveChangesAsync();

        await using var tenantAContext = fixture.CreateContext(fixture.TenantId, out var tenantA);
        await using var tenantBContext = fixture.CreateContext(tenantB, out var tenantBContextTenant);
        var tenantACalendar = new AttendanceFoundationService(tenantAContext, tenantA, new EffectiveEmploymentResolver(tenantAContext, tenantA), new WorkingDayCalendarResolver(tenantAContext, tenantA, new EffectiveEmploymentResolver(tenantAContext, tenantA)));
        var tenantBCalendar = new AttendanceFoundationService(tenantBContext, tenantBContextTenant, new EffectiveEmploymentResolver(tenantBContext, tenantBContextTenant), new WorkingDayCalendarResolver(tenantBContext, tenantBContextTenant, new EffectiveEmploymentResolver(tenantBContext, tenantBContextTenant)));
        Assert.Equal(fixture.TenantId, tenantA.TenantId);
        Assert.Equal(1, await tenantAContext.Employees.CountAsync(x => x.TenantId == fixture.TenantId));

        var tenantARows = await tenantACalendar.GetRosterAsync(new() { FromDate = new(2026, 8, 15), ToDate = new(2026, 10, 1), PageSize = 100 });
        Assert.DoesNotContain(tenantARows.Value!.Items, x => x.EmployeeCode == "B_EMP" || (x.EffectiveShiftCode?.StartsWith("B_", StringComparison.Ordinal) ?? false));
        Assert.Equal(aShift.ShiftCode, tenantARows.Value.Items.Single(x => x.RosterDate == new DateOnly(2026, 8, 15)).EffectiveShiftCode);

        var tenantBRows = await tenantBCalendar.GetRosterAsync(new() { FromDate = new(2026, 8, 15), ToDate = new(2026, 10, 1), EmployeeId = employeeB, PageSize = 100 });
        Assert.Contains(tenantBRows.Value!.Items, x => x.RosterDate == new DateOnly(2026, 8, 15) && x.DayType == RosterDayType.Holiday);
        Assert.Contains(tenantBRows.Value.Items, x => x.RosterDate == new DateOnly(2026, 8, 16) && x.DayType == RosterDayType.WeeklyOff);
        Assert.Contains(tenantBRows.Value.Items, x => x.RosterDate == new DateOnly(2026, 8, 17) && x.EffectiveShiftCode == "B_MANUAL" && x.AssignmentSource == RosterAssignmentSource.Manual);
        Assert.Contains(tenantBRows.Value.Items, x => x.RosterDate == new DateOnly(2026, 8, 18) && x.EffectiveShiftCode == "B_UPLOAD" && x.AssignmentSource == RosterAssignmentSource.Upload);
        Assert.Contains(tenantBRows.Value.Items, x => x.RosterDate == new DateOnly(2026, 10, 1) && x.EffectiveShiftCode == "B_EVENING" && x.AssignmentSource == RosterAssignmentSource.Auto);
    }
}
