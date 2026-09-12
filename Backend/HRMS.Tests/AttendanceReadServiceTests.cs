using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class AttendanceReadServiceTests
{
    [Fact]
    public async Task My_calendar_returns_every_day_and_planned_not_processed_state()
    {
        using var f = await CreateFixtureAsync();
        var date = new DateOnly(2026, 10, 10);

        var result = await Service(f).GetMyCalendarAsync(new() { Year = 2026, Month = 10 });

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(31, result.Value!.Count);
        var row = result.Value.Single(x => x.Date == date);
        Assert.Equal(EmployeeAttendanceDayStatus.NotProcessed, row.AttendanceStatus);
        Assert.Equal("READ", row.ShiftCode);
        Assert.Equal(RosterDayType.Shift, row.DayType);
        Assert.False(row.IsProcessed);
    }

    [Fact]
    public async Task My_calendar_exposes_processed_status_punch_summary_and_variances()
    {
        using var f = await CreateFixtureAsync();
        var date = new DateOnly(2026, 10, 10);
        f.Context.EmployeeAttendanceDays.Add(new EmployeeAttendanceDay
        {
            Id = Guid.NewGuid(), TenantId = f.TenantId, EmployeeId = f.EmployeeId, BusinessDate = date,
            ShiftId = f.Shift!.Id, ShiftCode = "READ", ExpectedWorkMinutes = 480,
            RosterAssignmentSource = RosterAssignmentSource.Auto, RosterDayType = RosterDayType.Shift,
            Status = EmployeeAttendanceDayStatus.Present, FirstPunchAtUtc = date.ToDateTime(new(9, 16), DateTimeKind.Utc),
            LastPunchAtUtc = date.ToDateTime(new(17, 40), DateTimeKind.Utc), PunchCount = 2, SessionCount = 1,
            WorkedMinutes = 504, IsLateIn = true, IsEarlyOut = true, LeaveConflict = true,
            ProcessedAtUtc = DateTime.UtcNow
        });
        await f.Context.SaveChangesAsync();

        var row = (await Service(f).GetMyCalendarAsync(new() { Year = 2026, Month = 10 })).Value!.Single(x => x.Date == date);
        Assert.Equal(EmployeeAttendanceDayStatus.Present, row.AttendanceStatus);
        Assert.Equal(2, row.PunchCount);
        Assert.Equal(504, row.WorkedMinutes);
        Assert.True(row.IsLateIn);
        Assert.True(row.IsEarlyOut);
        Assert.True(row.LeaveConflict);
        Assert.True(row.IsProcessed);
    }

    [Fact]
    public async Task My_day_returns_raw_punch_summary_and_paired_sessions()
    {
        using var f = await CreateFixtureAsync();
        var date = new DateOnly(2026, 10, 10);
        f.Context.AttendancePunches.AddRange(
            Punch(f, date, 9, PunchDirection.In), Punch(f, date, 13, PunchDirection.Out),
            Punch(f, date, 14, PunchDirection.In), Punch(f, date, 18, PunchDirection.Out));
        await f.Context.SaveChangesAsync();

        var result = await Service(f).GetMyDayAsync(date);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(4, result.Value!.Punches.Count);
        Assert.Equal(2, result.Value.Sessions.Count);
        Assert.Equal(240, result.Value.Sessions[0].WorkedMinutes);
        Assert.Equal(240, result.Value.Sessions[1].WorkedMinutes);
    }

    [Fact]
    public async Task Manager_team_is_date_scoped_and_filters_to_effective_reports()
    {
        using var f = await CreateFixtureAsync();
        var report = Guid.NewGuid();
        await f.AddEmployeeAsync(report, "REPORT");
        var date = new DateOnly(2026, 10, 10);
        var manager = new StubManagerResolver(f.EmployeeId);
        var result = await Service(f, manager).GetManagerTeamAsync(new() { FromDate = date, ToDate = date, Page = 1, PageSize = 20 });

        Assert.True(result.Succeeded, result.Message);
        Assert.Single(result.Value!.Rows.Items);
        Assert.Equal(report, result.Value.Rows.Items[0].EmployeeId);
    }

    [Fact]
    public async Task Manager_team_rejects_unbounded_ranges()
    {
        using var f = await CreateFixtureAsync();
        var result = await Service(f).GetManagerTeamAsync(new() { FromDate = new(2026, 1, 1), ToDate = new(2026, 4, 5) });
        Assert.Equal(ResultStatus.ValidationFailed, result.Status);
    }

    [Fact]
    public async Task Unlinked_identity_is_returned_as_explicit_not_found()
    {
        using var f = await CreateFixtureAsync();
        var service = new AttendanceReadService(f.Context, f.TenantContext, f.CalendarService, new StubIdentity(f.TenantId, null), new StubManagerResolver(f.EmployeeId));
        var result = await service.GetMyCalendarAsync(new() { Year = 2026, Month = 10 });
        Assert.Equal(ResultStatus.NotFound, result.Status);
    }

    private static AttendanceReadService Service(AttendanceTestFixture f, IEmployeeManagerResolver? managers = null) =>
        new(f.Context, f.TenantContext, f.CalendarService, new StubIdentity(f.TenantId, f.EmployeeId), managers ?? new StubManagerResolver(f.EmployeeId));

    private static AttendancePunch Punch(AttendanceTestFixture f, DateOnly date, int hour, PunchDirection direction) => new()
    {
        Id = Guid.NewGuid(), TenantId = f.TenantId, EmployeeId = f.EmployeeId,
        PunchAtUtc = date.ToDateTime(new(hour, 0), DateTimeKind.Utc), BusinessDate = date,
        Direction = direction, Source = PunchSource.Biometric, ExternalPunchId = Guid.NewGuid().ToString("N"), CapturedAtUtc = DateTime.UtcNow
    };

    private static async Task<AttendanceTestFixture> CreateFixtureAsync()
    {
        var f = await AttendanceTestFixture.CreateAsync();
        f.Shift = new Shift { Id = Guid.NewGuid(), TenantId = f.TenantId, ShiftCode = "READ", ShiftName = "Read Shift", IsDefault = true, IsActive = true, EffectiveFrom = new(2026, 1, 1), StartTime = new(9, 0), EndTime = new(18, 0), PlannedDurationMinutes = 540, FullDayWorkMinutes = 480, MinimumWorkMinutes = 1, CaptureMode = AttendanceCaptureMode.Mixed };
        f.Context.Shifts.Add(f.Shift);
        await f.Context.SaveChangesAsync();
        return f;
    }

    private sealed class StubIdentity(Guid tenantId, Guid? employeeId) : IEmployeeIdentityResolver
    {
        public Task<Result<RuntimeEmployeeIdentity>> ResolveCurrentAsync(CancellationToken cancellationToken = default) => employeeId is Guid id
            ? Task.FromResult(Result<RuntimeEmployeeIdentity>.Success(new(tenantId, Guid.NewGuid(), id)))
            : Task.FromResult(Result<RuntimeEmployeeIdentity>.NotFound("The authenticated account is not linked to an Employee."));
    }

    private sealed class StubManagerResolver(Guid managerId) : IEmployeeManagerResolver
    {
        public Task<Result<EmployeeManagerResolution>> ResolveAsync(Guid employeeId, DateOnly asOfDate, CancellationToken cancellationToken = default) =>
            Task.FromResult(employeeId == managerId
                ? Result<EmployeeManagerResolution>.Success(new(EmployeeManagerResolutionStatus.NoAssignedManager, employeeId, null, null, null, "manager"))
                : Result<EmployeeManagerResolution>.Success(new(EmployeeManagerResolutionStatus.Resolved, employeeId, managerId, "MGR", "Manager", "resolved")));
        public Task<bool> WouldCreateCycleAsync(Guid employeeId, Guid proposedManagerId, DateOnly asOfDate, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }
}
