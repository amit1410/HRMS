using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class AttendancePunchProcessingTests
{
    [Fact]
    public async Task In_and_out_punches_produce_present_day()
    {
        using var f = await CreateFixtureAsync();
        var date = new DateOnly(2026, 10, 10);
        await AddPunchesAsync(f, date, (9, 0, PunchDirection.In), (18, 0, PunchDirection.Out));

        var result = await Processor(f, date).ProcessAsync(f.EmployeeId, date);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(EmployeeAttendanceDayStatus.Present, result.Value!.Status);
        Assert.Equal(540, result.Value.WorkedMinutes);
        Assert.Equal(1, result.Value.SessionCount);
        Assert.Equal(2, result.Value.PunchCount);
    }

    [Fact]
    public async Task Multiple_sessions_are_summed_deterministically()
    {
        using var f = await CreateFixtureAsync();
        var date = new DateOnly(2026, 10, 10);
        await AddPunchesAsync(f, date, (9, 0, PunchDirection.In), (13, 0, PunchDirection.Out), (14, 0, PunchDirection.In), (18, 0, PunchDirection.Out));

        var result = await Processor(f, date).ProcessAsync(f.EmployeeId, date);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(480, result.Value!.WorkedMinutes);
        Assert.Equal(2, result.Value.SessionCount);
    }

    [Fact]
    public async Task External_punch_id_is_idempotent()
    {
        using var f = await CreateFixtureAsync();
        var ingestion = Ingestion(f);
        var request = new AttendancePunchIngestionRequest(f.EmployeeId, new DateTime(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc), PunchDirection.In, PunchSource.Biometric, "device-1");

        var first = await ingestion.IngestAsync(request);
        var second = await ingestion.IngestAsync(request);

        Assert.True(first.Succeeded, first.Message);
        Assert.True(second.Succeeded, second.Message);
        Assert.Equal(first.Value!.Id, second.Value!.Id);
        Assert.Equal(1, await f.Context.AttendancePunches.CountAsync());
    }

    [Theory]
    [InlineData(PunchSource.Biometric, AttendanceSource.Biometric, true)]
    [InlineData(PunchSource.Portal, AttendanceSource.Portal, true)]
    [InlineData(PunchSource.AutoLoginLogout, AttendanceSource.AutoLoginLogout, true)]
    [InlineData(PunchSource.Manual, AttendanceSource.Manual, true)]
    [InlineData(PunchSource.Biometric, AttendanceSource.Portal, false)]
    [InlineData(PunchSource.Portal, AttendanceSource.Biometric, false)]
    [InlineData(PunchSource.AutoLoginLogout, AttendanceSource.Biometric, false)]
    [InlineData(PunchSource.Manual, AttendanceSource.Biometric, false)]
    public async Task Punch_source_is_checked_against_effective_shift(PunchSource source, AttendanceSource allowed, bool expectedSuccess)
    {
        using var f = await CreateFixtureAsync(allowedSources: allowed);
        var result = await Ingestion(f).IngestAsync(new AttendancePunchIngestionRequest(
            f.EmployeeId, new DateTime(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc), PunchDirection.In, source, Guid.NewGuid().ToString("N")));

        Assert.Equal(expectedSuccess, result.Succeeded);
        if (!expectedSuccess)
            Assert.Equal(ResultStatus.Forbidden, result.Status);
    }

    [Fact]
    public async Task Disallowed_single_punch_is_incomplete()
    {
        using var f = await CreateFixtureAsync(allowSinglePunch: false);
        var date = new DateOnly(2026, 10, 10);
        await AddPunchesAsync(f, date, (9, 0, PunchDirection.In));

        var result = await Processor(f, date).ProcessAsync(f.EmployeeId, date);

        Assert.Equal(EmployeeAttendanceDayStatus.Incomplete, result.Value!.Status);
        Assert.True(result.Value.IsSinglePunch);
        Assert.True(result.Value.HasMissingOutPunch);
    }

    [Fact]
    public async Task Single_punch_allowed_does_not_fabricate_worked_minutes()
    {
        using var f = await CreateFixtureAsync(allowSinglePunch: true);
        var date = new DateOnly(2026, 10, 10);
        await AddPunchesAsync(f, date, (9, 0, PunchDirection.In));

        var result = await Processor(f, date).ProcessAsync(f.EmployeeId, date);

        Assert.Equal(EmployeeAttendanceDayStatus.Present, result.Value!.Status);
        Assert.Null(result.Value.WorkedMinutes);
        Assert.True(result.Value.IsSinglePunch);
    }

    [Fact]
    public async Task Mandatory_mark_out_missing_is_incomplete()
    {
        using var f = await CreateFixtureAsync(markOutMandatory: true);
        var date = new DateOnly(2026, 10, 10);
        await AddPunchesAsync(f, date, (9, 0, PunchDirection.In));

        var result = await Processor(f, date).ProcessAsync(f.EmployeeId, date);

        Assert.Equal(EmployeeAttendanceDayStatus.Incomplete, result.Value!.Status);
        Assert.True(result.Value.HasMissingOutPunch);
    }

    [Fact]
    public async Task Invalid_sequence_is_reported_without_throwing()
    {
        using var f = await CreateFixtureAsync();
        var date = new DateOnly(2026, 10, 10);
        await AddPunchesAsync(f, date, (9, 0, PunchDirection.Out), (10, 0, PunchDirection.In));

        var result = await Processor(f, date).ProcessAsync(f.EmployeeId, date);

        Assert.Equal(EmployeeAttendanceDayStatus.Incomplete, result.Value!.Status);
        Assert.True(result.Value.HasInvalidPunchSequence);
    }

    [Fact]
    public async Task Overnight_punches_share_the_shift_business_date()
    {
        using var f = await CreateFixtureAsync(start: new(22, 0), end: new(6, 0), crossesMidnight: true, maxPostShift: 15);
        var ingestion = Ingestion(f);
        var first = await ingestion.IngestAsync(new AttendancePunchIngestionRequest(f.EmployeeId, new DateTime(2026, 10, 10, 21, 55, 0, DateTimeKind.Utc), PunchDirection.In, PunchSource.Biometric, "overnight-in"));
        var last = await ingestion.IngestAsync(new AttendancePunchIngestionRequest(f.EmployeeId, new DateTime(2026, 10, 11, 6, 5, 0, DateTimeKind.Utc), PunchDirection.Out, PunchSource.Biometric, "overnight-out"));

        Assert.True(first.Succeeded, first.Message);
        Assert.True(last.Succeeded, last.Message);
        Assert.Equal(new DateOnly(2026, 10, 10), first.Value!.BusinessDate);
        Assert.Equal(new DateOnly(2026, 10, 10), last.Value!.BusinessDate);
    }

    [Fact]
    public async Task Late_boundary_allows_grace_but_late_after_boundary_is_flagged()
    {
        using var f = await CreateFixtureAsync(graceIn: 15);
        var date = new DateOnly(2026, 10, 10);
        await AddPunchesAsync(f, date, (9, 15, PunchDirection.In), (18, 0, PunchDirection.Out));
        var grace = await Processor(f, date).ProcessAsync(f.EmployeeId, date);
        Assert.False(grace.Value!.IsLateIn);
        Assert.True(grace.Value.IsGraceApplied);

        using var lateFixture = await CreateFixtureAsync(graceIn: 15);
        await AddPunchesAsync(lateFixture, date, (9, 16, PunchDirection.In), (18, 0, PunchDirection.Out));
        var late = await Processor(lateFixture, date).ProcessAsync(lateFixture.EmployeeId, date);
        Assert.True(late.Value!.IsLateIn);
    }

    [Fact]
    public async Task Early_out_boundary_uses_configured_grace()
    {
        using var f = await CreateFixtureAsync(graceOut: 10);
        var date = new DateOnly(2026, 10, 10);
        await AddPunchesAsync(f, date, (9, 0, PunchDirection.In), (17, 50, PunchDirection.Out));
        var inside = await Processor(f, date).ProcessAsync(f.EmployeeId, date);
        Assert.False(inside.Value!.IsEarlyOut);

        using var earlyFixture = await CreateFixtureAsync(graceOut: 10);
        await AddPunchesAsync(earlyFixture, date, (9, 0, PunchDirection.In), (17, 40, PunchDirection.Out));
        var early = await Processor(earlyFixture, date).ProcessAsync(earlyFixture.EmployeeId, date);
        Assert.True(early.Value!.IsEarlyOut);
    }

    [Fact]
    public async Task Unpaid_fixed_break_is_subtracted_but_paid_break_is_not()
    {
        var date = new DateOnly(2026, 10, 10);
        using var unpaid = await CreateFixtureAsync();
        unpaid.Context.ChangeTracker.Clear();
        unpaid.Context.ShiftBreaks.Add(new ShiftBreak { Id = Guid.NewGuid(), TenantId = unpaid.TenantId, ShiftId = unpaid.Shift!.Id, Name = "Lunch", StartTime = new(13, 0), EndTime = new(14, 0), Sequence = 1, IsPaid = false });
        await unpaid.Context.SaveChangesAsync();
        await AddPunchesAsync(unpaid, date, (9, 0, PunchDirection.In), (18, 0, PunchDirection.Out));
        var unpaidResult = await Processor(unpaid, date).ProcessAsync(unpaid.EmployeeId, date);

        using var paid = await CreateFixtureAsync();
        paid.Context.ChangeTracker.Clear();
        paid.Context.ShiftBreaks.Add(new ShiftBreak { Id = Guid.NewGuid(), TenantId = paid.TenantId, ShiftId = paid.Shift!.Id, Name = "Lunch", StartTime = new(13, 0), EndTime = new(14, 0), Sequence = 1, IsPaid = true });
        await paid.Context.SaveChangesAsync();
        await AddPunchesAsync(paid, date, (9, 0, PunchDirection.In), (18, 0, PunchDirection.Out));
        var paidResult = await Processor(paid, date).ProcessAsync(paid.EmployeeId, date);

        Assert.Equal(60, unpaidResult.Value!.BreakMinutes);
        Assert.Equal(480, unpaidResult.Value.WorkedMinutes);
        Assert.Equal(0, paidResult.Value!.BreakMinutes);
        Assert.Equal(540, paidResult.Value.WorkedMinutes);
    }

    [Fact]
    public async Task Reprocessing_after_out_updates_the_same_daily_row()
    {
        using var f = await CreateFixtureAsync(allowSinglePunch: false);
        var date = new DateOnly(2026, 10, 10);
        await AddPunchAsync(f, date, 9, 0, PunchDirection.In, "reprocess-in");
        var first = await Processor(f, date).ProcessAsync(f.EmployeeId, date);
        await AddPunchAsync(f, date, 18, 0, PunchDirection.Out, "reprocess-out");
        var second = await Processor(f, date).ProcessAsync(f.EmployeeId, date);

        Assert.Equal(EmployeeAttendanceDayStatus.Incomplete, first.Value!.Status);
        Assert.Equal(EmployeeAttendanceDayStatus.Present, second.Value!.Status);
        Assert.Equal(first.Value.Id, second.Value.Id);
        Assert.Equal(1, await f.Context.EmployeeAttendanceDays.CountAsync());
    }

    [Fact]
    public async Task Holiday_and_weekly_off_without_override_are_non_working_days()
    {
        using var holiday = await CreateFixtureAsync();
        var holidayDate = new DateOnly(2026, 10, 10);
        holiday.Shift!.IsDefault = false;
        await holiday.Context.SaveChangesAsync();
        await holiday.AddHolidayAsync(holidayDate);
        var holidayResult = await Processor(holiday, holidayDate).ProcessAsync(holiday.EmployeeId, holidayDate);
        Assert.Equal(EmployeeAttendanceDayStatus.Holiday, holidayResult.Value!.Status);

        using var weekly = await CreateFixtureAsync();
        var weeklyDate = new DateOnly(2026, 10, 11);
        weekly.Shift!.IsDefault = false;
        await weekly.Context.SaveChangesAsync();
        await weekly.AddWeeklyOffAsync(DayOfWeek.Sunday);
        var weeklyResult = await Processor(weekly, weeklyDate).ProcessAsync(weekly.EmployeeId, weeklyDate);
        Assert.Equal(EmployeeAttendanceDayStatus.WeeklyOff, weeklyResult.Value!.Status);
    }

    [Fact]
    public async Task Calendar_working_overrides_are_processed_as_working_shift()
    {
        using var f = await CreateFixtureAsync();
        var holidayDate = new DateOnly(2026, 10, 10);
        await f.AddHolidayAsync(holidayDate);
        f.Context.EmployeeRosterDays.Add(new EmployeeRosterDay { Id = Guid.NewGuid(), TenantId = f.TenantId, EmployeeId = f.EmployeeId, RosterDate = holidayDate, ShiftId = f.Shift!.Id, DayType = RosterDayType.Shift, AssignmentSource = RosterAssignmentSource.Manual, IsOverride = true, IsCalendarOverride = true, OriginalCalendarDayType = RosterCalendarDayType.Holiday });
        await f.Context.SaveChangesAsync(); await AddPunchesAsync(f, holidayDate, (9, 0, PunchDirection.In), (18, 0, PunchDirection.Out));
        var result = await Processor(f, holidayDate).ProcessAsync(f.EmployeeId, holidayDate);
        Assert.Equal(EmployeeAttendanceDayStatus.Present, result.Value!.Status);

        using var weekly = await CreateFixtureAsync();
        var weeklyDate = new DateOnly(2026, 10, 11); await weekly.AddWeeklyOffAsync(DayOfWeek.Sunday);
        weekly.Context.EmployeeRosterDays.Add(new EmployeeRosterDay { Id = Guid.NewGuid(), TenantId = weekly.TenantId, EmployeeId = weekly.EmployeeId, RosterDate = weeklyDate, ShiftId = weekly.Shift!.Id, DayType = RosterDayType.Shift, AssignmentSource = RosterAssignmentSource.Manual, IsOverride = true, IsCalendarOverride = true, OriginalCalendarDayType = RosterCalendarDayType.WeeklyOff });
        await weekly.Context.SaveChangesAsync(); await AddPunchesAsync(weekly, weeklyDate, (9, 0, PunchDirection.In), (18, 0, PunchDirection.Out));
        var weeklyResult = await Processor(weekly, weeklyDate).ProcessAsync(weekly.EmployeeId, weeklyDate);
        Assert.Equal(EmployeeAttendanceDayStatus.Present, weeklyResult.Value!.Status);
    }

    [Fact]
    public async Task Ingestion_reports_no_effective_shift_instead_of_creating_a_punch()
    {
        using var f = await CreateFixtureAsync(); f.Shift!.IsDefault = false; await f.Context.SaveChangesAsync();
        var result = await Ingestion(f).IngestAsync(new(f.EmployeeId, new DateTime(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc), PunchDirection.In, PunchSource.Biometric, "no-shift"));
        Assert.False(result.Succeeded); Assert.Equal(ResultStatus.Conflict, result.Status); Assert.Contains("NoEffectiveShift", result.Message);
        Assert.Empty(await f.Context.AttendancePunches.ToListAsync());
    }

    [Fact]
    public async Task Future_day_without_context_is_not_marked_absent()
    {
        using var f = await CreateFixtureAsync(end: new(23, 59));
        var result = await Processor(f, new DateOnly(2026, 10, 10)).ProcessAsync(f.EmployeeId, new DateOnly(2026, 10, 10));
        Assert.Equal(EmployeeAttendanceDayStatus.NotProcessed, result.Value!.Status);
    }

    [Fact]
    public async Task Punches_are_tenant_scoped_and_same_external_id_can_exist_in_other_tenant()
    {
        using var first = await CreateFixtureAsync();
        using var second = await CreateFixtureAsync();
        var external = "same-external-id";
        var requestDate = new DateTime(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc);
        var a = await Ingestion(first).IngestAsync(new(first.EmployeeId, requestDate, PunchDirection.In, PunchSource.Biometric, external));
        var b = await Ingestion(second).IngestAsync(new(second.EmployeeId, requestDate, PunchDirection.In, PunchSource.Biometric, external));

        Assert.True(a.Succeeded, a.Message);
        Assert.True(b.Succeeded, b.Message);
        Assert.NotEqual(a.Value!.Id, b.Value!.Id);
        Assert.Equal(1, await first.Context.AttendancePunches.CountAsync());
        Assert.Equal(1, await second.Context.AttendancePunches.CountAsync());
    }

    [Fact]
    public async Task Concurrent_processing_keeps_one_daily_aggregate()
    {
        using var f = await CreateFixtureAsync();
        var date = new DateOnly(2026, 10, 10);
        var (contextA, tenantA) = CreateProcessingContext(f);
        var (contextB, tenantB) = CreateProcessingContext(f);
        try
        {
            var first = new AttendanceDayProcessor(contextA, tenantA, new AttendanceFoundationService(contextA, tenantA, new EffectiveEmploymentResolver(contextA, tenantA), new WorkingDayCalendarResolver(contextA, tenantA, new EffectiveEmploymentResolver(contextA, tenantA))), new FixedClock(new DateTimeOffset(2026, 10, 10, 20, 0, 0, TimeSpan.Zero)));
            var second = new AttendanceDayProcessor(contextB, tenantB, new AttendanceFoundationService(contextB, tenantB, new EffectiveEmploymentResolver(contextB, tenantB), new WorkingDayCalendarResolver(contextB, tenantB, new EffectiveEmploymentResolver(contextB, tenantB))), new FixedClock(new DateTimeOffset(2026, 10, 10, 20, 0, 0, TimeSpan.Zero)));
            var results = await Task.WhenAll(first.ProcessAsync(f.EmployeeId, date), second.ProcessAsync(f.EmployeeId, date));
            Assert.All(results, result => Assert.True(result.Succeeded, result.Message));
            Assert.Equal(1, await f.Context.EmployeeAttendanceDays.CountAsync(x => x.EmployeeId == f.EmployeeId && x.BusinessDate == date));
        }
        finally { await contextA.DisposeAsync(); await contextB.DisposeAsync(); }
    }

    private static AttendancePunchIngestionService Ingestion(AttendanceTestFixture f) =>
        new(f.Context, f.TenantContext, f.CalendarService, new AttendanceBusinessDateResolver(), new AttendanceBusinessTimeZoneProvider(), new FixedClock(new DateTimeOffset(2026, 10, 10, 20, 0, 0, TimeSpan.Zero)));

    private static (HRMS.Infrastructure.Persistence.HrmsDbContext Context, TestTenantContext Tenant) CreateProcessingContext(AttendanceTestFixture f)
    {
        var context = f.CreateContext(f.TenantId, out var tenant);
        return (context, tenant);
    }

    private static AttendanceDayProcessor Processor(AttendanceTestFixture f, DateOnly date) =>
        new(f.Context, f.TenantContext, f.CalendarService, new FixedClock(new DateTimeOffset(date.ToDateTime(new(20, 0)), TimeSpan.Zero)));

    private static async Task<AttendanceTestFixture> CreateFixtureAsync(
        AttendanceSource allowedSources = AttendanceSource.Biometric | AttendanceSource.Portal | AttendanceSource.AutoLoginLogout | AttendanceSource.Manual,
        bool allowSinglePunch = false, bool markOutMandatory = false, int graceIn = 0, int graceOut = 0,
        TimeOnly? start = null, TimeOnly? end = null, bool crossesMidnight = false, int maxPostShift = 0, FixedClock? clock = null)
    {
        var f = await AttendanceTestFixture.CreateAsync();
        f.Shift = new Shift
        {
            Id = Guid.NewGuid(), TenantId = f.TenantId, ShiftCode = "PROCESS", ShiftName = "PROCESS",
            EffectiveFrom = new(2026, 1, 1), IsDefault = true, StartTime = start ?? new(9, 0), EndTime = end ?? new(18, 0),
            PlannedDurationMinutes = crossesMidnight ? 480 : 540, FullDayWorkMinutes = 480, MinimumWorkMinutes = 1,
            IsActive = true, AllowedAttendanceSources = allowedSources, CaptureMode = AttendanceCaptureMode.Mixed,
            AllowPresentOnSinglePunch = allowSinglePunch, IsMarkOutMandatory = markOutMandatory,
            GraceInMinutes = graceIn, GraceOutMinutes = graceOut, CrossesMidnight = crossesMidnight,
            PostShiftMarkOutMode = PostShiftMarkOutMode.Allowed, MaximumPostShiftMinutes = maxPostShift
        };
        f.Context.Shifts.Add(f.Shift);
        await f.Context.SaveChangesAsync();
        return f;
    }

    private static async Task AddPunchesAsync(AttendanceTestFixture f, DateOnly date, params (int hour, int minute, PunchDirection direction)[] punches)
    {
        foreach (var punch in punches)
            await AddPunchAsync(f, date, punch.hour, punch.minute, punch.direction, Guid.NewGuid().ToString("N"));
    }

    private static async Task AddPunchAsync(AttendanceTestFixture f, DateOnly date, int hour, int minute, PunchDirection direction, string externalId)
    {
        f.Context.AttendancePunches.Add(new AttendancePunch
        {
            Id = Guid.NewGuid(), TenantId = f.TenantId, EmployeeId = f.EmployeeId,
            PunchAtUtc = new DateTime(date.Year, date.Month, date.Day, hour, minute, 0, DateTimeKind.Utc),
            BusinessDate = date, Direction = direction, Source = PunchSource.Biometric,
            ExternalPunchId = externalId, CapturedAtUtc = new DateTime(2026, 10, 10, 20, 0, 0, DateTimeKind.Utc)
        });
        await f.Context.SaveChangesAsync();
    }
}
