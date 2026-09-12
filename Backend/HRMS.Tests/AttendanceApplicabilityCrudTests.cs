using HRMS.Application.Common;
using HRMS.Application.DTOs.Attendance;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class AttendanceApplicabilityCrudTests
{
    [Fact]
    public async Task Create_list_detail_and_update_preserve_rule_name_and_target()
    {
        using var fixture = await AttendanceTestFixture.CreateAsync();
        var shift = await fixture.AddShiftAsync("MORNING");
        var request = new ShiftApplicabilityRequest
        {
            RuleName = "  Noida rule  ", ShiftId = shift.Id, DepartmentId = fixture.DepartmentId,
            Priority = 10, EffectiveFrom = new(2026, 1, 1)
        };

        var created = await fixture.Service.AddApplicabilityAsync(request);
        Assert.True(created.Succeeded, created.Message);
        var listed = await fixture.Service.GetApplicabilityAsync(new());
        var rule = Assert.Single(listed.Value!.Items);

        Assert.Equal("Noida rule", rule.RuleName);
        Assert.Equal(shift.Id, rule.ShiftId);
        Assert.Equal(fixture.DepartmentId, rule.Conditions["Department"]);

        var update = new ShiftApplicabilityRequest
        {
            RuleName = "  Updated rule ", ShiftId = null, DepartmentId = fixture.DepartmentId,
            Priority = 20, EffectiveFrom = new(2026, 1, 1)
        };
        var pattern = new HRMS.Domain.Entities.ShiftPattern { Id = Guid.NewGuid(), TenantId = fixture.TenantId, Code = "PAT-CRUD", Name = "PAT-CRUD", CycleLengthDays = 1, EffectiveFrom = new(2026, 1, 1) };
        pattern.Days.Add(new HRMS.Domain.Entities.ShiftPatternDay { Id = Guid.NewGuid(), TenantId = fixture.TenantId, SequenceDay = 1, ShiftId = shift.Id, DayType = HRMS.Domain.Enums.ShiftPatternDayType.Shift });
        fixture.Context.ShiftPatterns.Add(pattern);
        await fixture.Context.SaveChangesAsync();
        update.ShiftPatternId = pattern.Id;

        var updated = await fixture.Service.UpdateApplicabilityAsync(rule.Id, update);
        Assert.True(updated.Succeeded, updated.Message);
        var detail = await fixture.Service.GetApplicabilityByIdAsync(rule.Id);
        Assert.Equal("Updated rule", detail.Value!.RuleName);
        Assert.Null(detail.Value.ShiftId);
        Assert.Equal(pattern.Id, detail.Value.ShiftPatternId);
        Assert.Equal(20, detail.Value.Priority);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Rule_name_is_required(string? name)
    {
        using var fixture = await AttendanceTestFixture.CreateAsync();
        var shift = await fixture.AddShiftAsync("SHIFT");
        var result = await fixture.Service.AddApplicabilityAsync(new() { RuleName = name!, ShiftId = shift.Id, EffectiveFrom = new(2026, 1, 1) });
        Assert.False(result.Succeeded);
        Assert.Equal(ResultStatus.ValidationFailed, result.Status);
    }

    [Fact]
    public async Task Delete_removes_the_rule_within_the_tenant_scope()
    {
        using var fixture = await AttendanceTestFixture.CreateAsync();
        var shift = await fixture.AddShiftAsync("SHIFT");
        await fixture.AddRuleAsync(shift.Id, ruleName: "To delete");
        var rule = Assert.Single((await fixture.Service.GetApplicabilityAsync(new())).Value!.Items);

        var deleted = await fixture.Service.DeleteApplicabilityAsync(rule.Id);
        var detail = await fixture.Service.GetApplicabilityByIdAsync(rule.Id);

        Assert.True(deleted.Succeeded);
        Assert.False(detail.Succeeded);
        Assert.Equal(ResultStatus.NotFound, detail.Status);
    }

    [Fact]
    public async Task Applicability_range_crossing_a_closed_month_is_rejected_without_partial_write()
    {
        using var fixture = await AttendanceTestFixture.CreateAsync();
        var shift = await fixture.AddShiftAsync("LOCKED");
        fixture.Context.AttendancePeriods.Add(new AttendancePeriod
        {
            Id = Guid.NewGuid(), TenantId = fixture.TenantId, Year = 2026, Month = 9,
            StartDate = new(2026, 9, 1), EndDate = new(2026, 9, 30), Status = AttendancePeriodStatus.Closed
        });
        await fixture.Context.SaveChangesAsync();
        var guarded = new AttendanceFoundationService(fixture.Context, fixture.TenantContext,
            new EffectiveEmploymentResolver(fixture.Context, fixture.TenantContext),
            new WorkingDayCalendarResolver(fixture.Context, fixture.TenantContext, new EffectiveEmploymentResolver(fixture.Context, fixture.TenantContext)),
            periodLock: new AttendancePeriodLockService(fixture.Context, fixture.TenantContext));

        var result = await guarded.AddApplicabilityAsync(new()
        {
            RuleName = "Cross-month locked range", ShiftId = shift.Id,
            EffectiveFrom = new(2026, 9, 15), EffectiveTo = new(2026, 10, 15)
        });

        Assert.Equal(ResultStatus.Conflict, result.Status);
        Assert.Empty(await fixture.Context.ShiftApplicabilityRules.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Reopening_a_closed_month_restores_applicability_mutation_without_partial_split()
    {
        using var fixture = await AttendanceTestFixture.CreateAsync();
        var shift = await fixture.AddShiftAsync("REOPEN");
        var periodId = Guid.NewGuid();
        fixture.Context.AttendancePeriods.Add(new AttendancePeriod
        {
            Id = periodId, TenantId = fixture.TenantId, Year = 2026, Month = 9,
            StartDate = new(2026, 9, 1), EndDate = new(2026, 9, 30), Status = AttendancePeriodStatus.Closed
        });
        fixture.Context.Users.Add(new User { Id = fixture.TenantContext.UserId!.Value, TenantId = fixture.TenantId, Email = "reopen@example.test", FirstName = "Reopen", LastName = "Tester" });
        await fixture.Context.SaveChangesAsync();
        var monthly = new AttendanceMonthlyProcessor(fixture.Context, fixture.TenantContext);
        var reopened = await monthly.ReopenAsync(periodId, new("Historical applicability correction"));
        Assert.True(reopened.Succeeded, reopened.Message);
        var guarded = new AttendanceFoundationService(fixture.Context, fixture.TenantContext,
            new EffectiveEmploymentResolver(fixture.Context, fixture.TenantContext),
            new WorkingDayCalendarResolver(fixture.Context, fixture.TenantContext, new EffectiveEmploymentResolver(fixture.Context, fixture.TenantContext)),
            periodLock: new AttendancePeriodLockService(fixture.Context, fixture.TenantContext));

        var result = await guarded.AddApplicabilityAsync(new()
        {
            RuleName = "Reopened range", ShiftId = shift.Id,
            EffectiveFrom = new(2026, 9, 15), EffectiveTo = new(2026, 10, 15)
        });

        Assert.True(result.Succeeded, result.Message);
        Assert.Single(await fixture.Context.ShiftApplicabilityRules.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Applicability_reopen_reprocess_summary_and_reclose_lifecycle_is_consistent()
    {
        using var fixture = await AttendanceTestFixture.CreateAsync();
        fixture.Context.Users.Add(new User { Id = fixture.TenantContext.UserId!.Value, TenantId = fixture.TenantId, Email = "lifecycle@example.test", FirstName = "Lifecycle", LastName = "Tester" });
        var initialShift = await fixture.AddShiftAsync("LIFE-INITIAL", isDefault: true);
        var changedShift = await fixture.AddShiftAsync("LIFE-CHANGED");
        changedShift.FullDayWorkMinutes = 360;
        await fixture.Context.SaveChangesAsync();
        var date = new DateOnly(2026, 9, 10);
        var createdRule = await fixture.Service.AddApplicabilityAsync(new()
        {
            RuleName = "Lifecycle rule", ShiftId = initialShift.Id, EmployeeId = fixture.EmployeeId,
            EffectiveFrom = new(2026, 1, 1)
        });
        Assert.True(createdRule.Succeeded, createdRule.Message);
        var rule = await fixture.Context.ShiftApplicabilityRules.SingleAsync(x => x.RuleName == "Lifecycle rule");
        for (var day = new DateOnly(2026, 9, 1); day <= new DateOnly(2026, 9, 30); day = day.AddDays(1))
            fixture.Context.EmployeeAttendanceDays.Add(new EmployeeAttendanceDay { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, BusinessDate = day, Status = day == date ? EmployeeAttendanceDayStatus.Present : EmployeeAttendanceDayStatus.Holiday, ExpectedWorkMinutes = day == date ? 480 : null, WorkedMinutes = day == date ? 480 : null, ProcessedAtUtc = DateTime.UtcNow });
        await fixture.Context.SaveChangesAsync();

        var initialProcessor = new AttendanceDayProcessor(fixture.Context, fixture.TenantContext, fixture.Service, new FixedClock(new DateTimeOffset(2026, 9, 10, 20, 0, 0, TimeSpan.Zero)));
        Assert.True((await initialProcessor.ProcessAsync(fixture.EmployeeId, date)).Succeeded);
        var monthly = new AttendanceMonthlyProcessor(fixture.Context, fixture.TenantContext);
        var period = (await monthly.CreatePeriodAsync(new(2026, 9))).Value!;
        Assert.True((await monthly.ProcessAsync(period.Id)).Succeeded);
        var originalSummary = await fixture.Context.EmployeeAttendanceMonthlySummaries.AsNoTracking().SingleAsync(x => x.AttendancePeriodId == period.Id);
        var originalDay = await fixture.Context.EmployeeAttendanceDays.AsNoTracking().SingleAsync(x => x.BusinessDate == date);
        Assert.Equal(originalSummary.SourceDataVersion, await fixture.Context.AttendancePeriods.Where(x => x.Id == period.Id).Select(x => x.DataVersion).SingleAsync());
        Assert.True((await monthly.CloseAsync(period.Id)).Succeeded);
        var closedVersion = await fixture.Context.AttendancePeriods.Where(x => x.Id == period.Id).Select(x => x.DataVersion).SingleAsync();
        var lifecycleEmployment = new EffectiveEmploymentResolver(fixture.Context, fixture.TenantContext);
        var lifecycleCalendar = new WorkingDayCalendarResolver(fixture.Context, fixture.TenantContext, lifecycleEmployment);
        var guardedService = new AttendanceFoundationService(fixture.Context, fixture.TenantContext, lifecycleEmployment, lifecycleCalendar, periodLock: new AttendancePeriodLockService(fixture.Context, fixture.TenantContext));

        var blocked = await guardedService.UpdateApplicabilityAsync(rule.Id, new() { RuleName = "Lifecycle rule", ShiftId = changedShift.Id, EmployeeId = fixture.EmployeeId, EffectiveFrom = new(2026, 1, 1) });
        Assert.Equal(ResultStatus.Conflict, blocked.Status);
        Assert.Equal(closedVersion, await fixture.Context.AttendancePeriods.Where(x => x.Id == period.Id).Select(x => x.DataVersion).SingleAsync());
        Assert.Equal(originalDay.Status, await fixture.Context.EmployeeAttendanceDays.Where(x => x.BusinessDate == date).Select(x => x.Status).SingleAsync());

        var reopened = await monthly.ReopenAsync(period.Id, new("Update effective applicability"));
        Assert.True(reopened.Succeeded, reopened.Message);
        var changed = await guardedService.UpdateApplicabilityAsync(rule.Id, new() { RuleName = "Lifecycle rule", ShiftId = changedShift.Id, EmployeeId = fixture.EmployeeId, EffectiveFrom = new(2026, 1, 1) });
        Assert.True(changed.Succeeded, changed.Message);
        var dayContext = fixture.CreateContext(fixture.TenantId, out var dayTenant);
        await using (dayContext)
        {
            var employment = new EffectiveEmploymentResolver(dayContext, dayTenant);
            var calendar = new WorkingDayCalendarResolver(dayContext, dayTenant, employment);
            var roster = new AttendanceFoundationService(dayContext, dayTenant, employment, calendar, periodLock: new AttendancePeriodLockService(dayContext, dayTenant));
            var processed = await new AttendanceDayProcessor(dayContext, dayTenant, roster, new FixedClock(new DateTimeOffset(2026, 9, 10, 20, 0, 0, TimeSpan.Zero)), new AttendancePeriodLockService(dayContext, dayTenant)).ProcessAsync(fixture.EmployeeId, date);
            Assert.True(processed.Succeeded, processed.Message);
            Assert.Equal(changedShift.Id, processed.Value!.ShiftId);
            Assert.Equal(360, processed.Value.ExpectedWorkMinutes);
        }
        await using var refreshContext = fixture.CreateContext(fixture.TenantId, out var refreshTenant);
        var refreshMonthly = new AttendanceMonthlyProcessor(refreshContext, refreshTenant);
        var currentPeriod = await refreshContext.AttendancePeriods.SingleAsync(x => x.Id == period.Id);
        var staleSummary = await refreshContext.EmployeeAttendanceMonthlySummaries.AsNoTracking().SingleAsync(x => x.AttendancePeriodId == period.Id);
        Assert.NotEqual(staleSummary.SourceDataVersion, currentPeriod.DataVersion);
        var refreshedProcess = await refreshMonthly.ProcessAsync(period.Id);
        Assert.True(refreshedProcess.Succeeded, refreshedProcess.Message);
        var refreshed = await refreshContext.EmployeeAttendanceMonthlySummaries.AsNoTracking().SingleAsync(x => x.AttendancePeriodId == period.Id);
        currentPeriod = await refreshContext.AttendancePeriods.AsNoTracking().SingleAsync(x => x.Id == period.Id);
        Assert.Equal(currentPeriod.DataVersion, refreshed.SourceDataVersion);
        Assert.NotEqual(originalSummary.ExpectedWorkMinutes, refreshed.ExpectedWorkMinutes);
        Assert.Equal(1, await refreshContext.EmployeeAttendanceMonthlySummaries.CountAsync(x => x.AttendancePeriodId == period.Id));
        Assert.Equal(AttendancePeriodStatus.ReadyToClose, currentPeriod.Status);
        Assert.True((await refreshMonthly.CloseAsync(period.Id)).Succeeded);
        var events = await refreshContext.AttendancePeriodEvents.AsNoTracking().Where(x => x.AttendancePeriodId == period.Id).OrderBy(x => x.OccurredAtUtc).ThenBy(x => x.Id).Select(x => x.EventType).ToListAsync();
        Assert.Equal(2, events.Count(x => x == AttendancePeriodEventType.Closed));
        Assert.Equal(1, events.Count(x => x == AttendancePeriodEventType.Reopened));
        Assert.True(events.IndexOf(AttendancePeriodEventType.Reopened) > events.IndexOf(AttendancePeriodEventType.Closed));
        Assert.Equal(AttendancePeriodStatus.Closed, await refreshContext.AttendancePeriods.Where(x => x.Id == period.Id).Select(x => x.Status).SingleAsync());
    }
}
