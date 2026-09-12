using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit.Sdk;

namespace HRMS.Tests;

[Collection("Attendance MySQL")]
public sealed class MySqlAttendanceCalendarIntegrationTests
{
    [Fact]
    public async Task Real_mysql_employee_calendar_returns_processed_and_planned_days()
    {
        await WithFixture(async f =>
        {
            await using var db = f.CreateContext();
            var shift = AddShift(f.TenantId, "CAL"); db.Shifts.Add(shift);
            db.AttendancePunches.AddRange(
                Punch(f, new DateTime(2026, 10, 10, 9, 0, 0), PunchDirection.In),
                Punch(f, new DateTime(2026, 10, 10, 18, 0, 0), PunchDirection.Out));
            await db.SaveChangesAsync();
            var processor = Processor(db, f);
            var processed = await processor.ProcessAsync(f.EmployeeId, new(2026, 10, 10));
            Assert.True(processed.Succeeded, processed.Message);

            var read = ReadService(db, f);
            var result = await read.GetMyCalendarAsync(new() { Year = 2026, Month = 10 });
            Assert.True(result.Succeeded, result.Message);
            Assert.Equal(31, result.Value!.Count);
            Assert.Equal(EmployeeAttendanceDayStatus.Present, result.Value.Single(x => x.Date == new DateOnly(2026, 10, 10)).AttendanceStatus);
            Assert.Equal(EmployeeAttendanceDayStatus.NotProcessed, result.Value.Single(x => x.Date == new DateOnly(2026, 10, 11)).AttendanceStatus);
        });
    }

    [Fact]
    public async Task Real_mysql_employee_day_returns_punch_summary_and_sessions()
    {
        await WithFixture(async f =>
        {
            await using var db = f.CreateContext(); db.Shifts.Add(AddShift(f.TenantId, "DETAIL"));
            db.AttendancePunches.AddRange(Punch(f, new DateTime(2026, 10, 12, 9, 0, 0), PunchDirection.In), Punch(f, new DateTime(2026, 10, 12, 18, 0, 0), PunchDirection.Out)); await db.SaveChangesAsync();
            Assert.True((await Processor(db, f).ProcessAsync(f.EmployeeId, new(2026, 10, 12))).Succeeded);
            var result = await ReadService(db, f).GetMyDayAsync(new(2026, 10, 12));
            Assert.True(result.Succeeded, result.Message); Assert.Equal(2, result.Value!.Punches.Count); Assert.Single(result.Value.Sessions); Assert.Equal(EmployeeAttendanceDayStatus.Present, result.Value.Day.AttendanceStatus);
        });
    }

    [Fact]
    public async Task Real_mysql_manager_team_query_is_bounded_and_date_aware()
    {
        await WithFixture(async f =>
        {
            await using var db = f.CreateContext(); db.Shifts.Add(AddShift(f.TenantId, "TEAM")); await db.SaveChangesAsync();
            var result = await ReadService(db, f, manager: true).GetManagerTeamAsync(new() { FromDate = new(2026, 10, 10), ToDate = new(2026, 10, 11), PageSize = 20 });
            Assert.True(result.Succeeded, result.Message); Assert.NotNull(result.Value); Assert.True(result.Value!.Rows.TotalCount >= 0); Assert.Equal(20, result.Value.Rows.PageSize);
        });
    }

    [Fact]
    public async Task Real_mysql_calendar_projects_statuses_calendar_days_and_variances()
    {
        await WithFixture(async f =>
        {
            await using var db = f.CreateContext(); var stateShift = AddShift(f.TenantId, "STATE"); stateShift.IsDefault = false; db.Shifts.Add(stateShift);
            db.Holidays.Add(new Holiday { Id = Guid.NewGuid(), TenantId = f.TenantId, Name = "Calendar holiday", Date = new(2026, 10, 20), IsActive = true });
            var weekly = new WeeklyOffConfiguration { Id = Guid.NewGuid(), TenantId = f.TenantId, EffectiveFrom = new(2026, 1, 1), EffectiveTo = new(2026, 12, 31), IsActive = true };
            weekly.Days.Add(new WeeklyOffDay { Id = Guid.NewGuid(), TenantId = f.TenantId, WeeklyOffConfigurationId = weekly.Id, DayOfWeek = DayOfWeek.Wednesday }); db.WeeklyOffConfigurations.Add(weekly);
            db.EmployeeAttendanceDays.AddRange(
                Day(f, new(2026, 10, 10), EmployeeAttendanceDayStatus.Present, late: true, early: true, grace: true, conflict: true, shiftId: stateShift.Id),
                Day(f, new(2026, 10, 11), EmployeeAttendanceDayStatus.Absent, shiftId: stateShift.Id),
                Day(f, new(2026, 10, 12), EmployeeAttendanceDayStatus.Incomplete, missingOut: true, invalid: true, shiftId: stateShift.Id));
            db.LeaveRequests.Add(new LeaveRequest { Id = Guid.NewGuid(), TenantId = f.TenantId, EmployeeId = f.EmployeeId, LeaveTypeId = f.LeaveTypeId, LeavePeriodId = f.LeavePeriodId, LeavePolicyVersionId = f.PolicyVersionId, LeavePolicyRuleId = f.PolicyRuleId, EmployeeEmploymentHistoryId = f.EmployeeHistoryId, StartDate = new(2026, 10, 13), EndDate = new(2026, 10, 13), RequestedQuantity = 1, ChargeableQuantity = 1, Status = LeaveRequestStatus.Approved, SubmittedAtUtc = DateTime.UtcNow, IdempotencyKey = $"calendar-{Guid.NewGuid():N}", PayloadFingerprint = Guid.NewGuid().ToString("N"), Days = [new LeaveRequestDay { Id = Guid.NewGuid(), TenantId = f.TenantId, Date = new(2026, 10, 13), RequestedQuantity = 1, ChargeableQuantity = 1 }] });
            await db.SaveChangesAsync();
            var result = await ReadService(db, f).GetMyCalendarAsync(new() { Year = 2026, Month = 10 }); Assert.True(result.Succeeded, result.Message);
            var present = result.Value!.Single(x => x.Date.Equals(new DateOnly(2026, 10, 10)));
            Assert.Equal(EmployeeAttendanceDayStatus.Present, present.AttendanceStatus);
            Assert.True(present.IsLateIn); Assert.True(present.LeaveConflict);
            Assert.Equal(EmployeeAttendanceDayStatus.Absent, result.Value.Single(x => x.Date.Equals(new DateOnly(2026, 10, 11))).AttendanceStatus);
            var incomplete = result.Value.Single(x => x.Date.Equals(new DateOnly(2026, 10, 12)));
            Assert.True(incomplete.HasMissingOutPunch); Assert.True(incomplete.HasInvalidPunchSequence);
            Assert.Equal(EmployeeAttendanceDayStatus.OnLeave, result.Value.Single(x => x.Date.Equals(new DateOnly(2026, 10, 13))).AttendanceStatus);
            Assert.Equal(EmployeeAttendanceDayStatus.Holiday, result.Value.Single(x => x.Date.Equals(new DateOnly(2026, 10, 20))).AttendanceStatus);
            Assert.Equal(EmployeeAttendanceDayStatus.WeeklyOff, result.Value.Single(x => x.Date.Equals(new DateOnly(2026, 10, 21))).AttendanceStatus);
        });
    }

    [Fact]
    public async Task Real_mysql_manager_filters_and_pagination_are_server_side()
    {
        await WithFixture(async f =>
        {
            await using var db = f.CreateContext(); var shift = AddShift(f.TenantId, "FILTER"); db.Shifts.Add(shift); db.EmployeeAttendanceDays.AddRange(
                Day(f, new(2026, 10, 10), EmployeeAttendanceDayStatus.Present, late: true, shiftId: shift.Id),
                Day(f, new(2026, 10, 11), EmployeeAttendanceDayStatus.Absent, shiftId: shift.Id),
                Day(f, new(2026, 10, 12), EmployeeAttendanceDayStatus.OnLeave, shiftId: shift.Id),
                Day(f, new(2026, 10, 13), EmployeeAttendanceDayStatus.Incomplete, invalid: true, shiftId: shift.Id),
                Day(f, new(2026, 10, 14), EmployeeAttendanceDayStatus.NotProcessed, shiftId: shift.Id)); await db.SaveChangesAsync();
            var service = ReadService(db, f, manager: true);
            foreach (var status in Enum.GetValues<EmployeeAttendanceDayStatus>().Where(x => x != EmployeeAttendanceDayStatus.Holiday && x != EmployeeAttendanceDayStatus.WeeklyOff && x != EmployeeAttendanceDayStatus.OnDuty))
            {
                var filtered = await service.GetManagerTeamAsync(new() { FromDate = new(2026, 10, 10), ToDate = new(2026, 10, 14), Status = status, PageSize = 10 });
                Assert.True(filtered.Succeeded, filtered.Message); Assert.Single(filtered.Value!.Rows.Items); Assert.Equal(status, filtered.Value.Rows.Items[0].Day.AttendanceStatus);
            }
            var variance = await service.GetManagerTeamAsync(new() { FromDate = new(2026, 10, 10), ToDate = new(2026, 10, 14), Variance = "late", PageSize = 10 });
            Assert.True(variance.Succeeded, variance.Message); Assert.Single(variance.Value!.Rows.Items); Assert.True(variance.Value.Rows.Items[0].Day.IsLateIn);
            var page = await service.GetManagerTeamAsync(new() { FromDate = new(2026, 10, 14), ToDate = new(2026, 10, 10), Page = 2, PageSize = 1 });
            Assert.False(page.Succeeded);
            var validPage = await service.GetManagerTeamAsync(new() { FromDate = new(2026, 10, 10), ToDate = new(2026, 10, 14), Page = 2, PageSize = 2 });
            Assert.True(validPage.Succeeded, validPage.Message); Assert.Equal(5, validPage.Value!.Rows.TotalCount); Assert.Equal(2, validPage.Value.Rows.Items.Count); Assert.Equal(5, validPage.Value.Summary.Present + validPage.Value.Summary.Absent + validPage.Value.Summary.OnLeave + validPage.Value.Summary.Incomplete + validPage.Value.Summary.NotProcessed);
        });
    }

    [Fact]
    public async Task Real_mysql_self_service_identity_and_tenant_scope_are_enforced()
    {
        await WithFixture(async f =>
        {
            await using var db = f.CreateContext(); db.Shifts.Add(AddShift(f.TenantId, "IDENTITY")); await db.SaveChangesAsync();
            var own = await ReadService(db, f).GetMyDayAsync(new(2026, 10, 10)); Assert.True(own.Succeeded, own.Message); Assert.Equal(new DateOnly(2026, 10, 10), own.Value!.Day.Date);
            await using var otherDb = f.CreateContext(new TestTenantContext(f.OtherTenantId, Guid.NewGuid()));
            var other = await ReadService(otherDb, f, manager: false).GetMyDayAsync(new(2026, 10, 10)); Assert.False(other.Succeeded); Assert.Equal(HRMS.Application.Common.ResultStatus.NotFound, other.Status);
        });
    }

    [Fact]
    public async Task Real_mysql_manager_access_uses_effective_manager_history()
    {
        await WithFixture(async f =>
        {
            var managerBId = Guid.NewGuid(); var managerBUserId = Guid.NewGuid();
            await using var db = f.CreateContext();
            var shift = AddShift(f.TenantId, "HISTORY");
            db.Shifts.Add(shift);
            var originalHistory = await db.EmployeeEmploymentHistory.SingleAsync(x => x.Id == f.EmployeeHistoryId);
            originalHistory.EffectiveTo = new(2026, 10, 14);
            db.Employees.Add(new Employee { Id = managerBId, TenantId = f.TenantId, EmployeeCode = $"MB{managerBId:N}"[..8], FirstName = "History", LastName = "Manager", DateOfJoining = new(2020, 1, 1), Status = EmployeeStatus.Active });
            db.Users.Add(new User { Id = managerBUserId, TenantId = f.TenantId, Email = $"history-manager-{managerBUserId:N}@test.invalid", PasswordHash = "test", FirstName = "History", LastName = "Manager", IsActive = true });
            var managerBLinkId = Guid.NewGuid();
            db.AccountEmployeeCurrentLinks.Add(new AccountEmployeeCurrentLink { LinkId = managerBLinkId, TenantId = f.TenantId, UserId = managerBUserId, EmployeeId = managerBId });
            db.AccountEmployeeLinkEvents.Add(new AccountEmployeeLinkEvent { Id = managerBLinkId, TenantId = f.TenantId, SubjectUserId = managerBUserId, ActorUserId = managerBUserId, Sequence = 1, Operation = "Link", NewLinkId = managerBLinkId, AfterEmployeeId = managerBId, OccurredAtUtc = DateTime.UtcNow, Reason = "MySQL calendar test", CorrelationId = $"manager-history-{managerBLinkId:N}" });
            db.EmployeeEmploymentHistory.Add(new EmployeeEmploymentHistory { Id = Guid.NewGuid(), TenantId = f.TenantId, EmployeeId = f.EmployeeId, EffectiveFrom = new(2026, 10, 15), ManagerId = managerBId, EmploymentStatus = EmployeeStatus.Active });
            db.EmployeeAttendanceDays.AddRange(Day(f, new(2026, 10, 10), EmployeeAttendanceDayStatus.Present, shiftId: shift.Id), Day(f, new(2026, 10, 16), EmployeeAttendanceDayStatus.Present, shiftId: shift.Id));
            await db.SaveChangesAsync();
            var managerA = await ReadService(db, f, manager: true).GetManagerTeamAsync(new() { FromDate = new(2026, 10, 10), ToDate = new(2026, 10, 10), PageSize = 10 });
            Assert.True(managerA.Succeeded, managerA.Message); Assert.Single(managerA.Value!.Rows.Items);
            var managerB = await ReadService(db, f, context: new TestTenantContext(f.TenantId, managerBUserId)).GetManagerTeamAsync(new() { FromDate = new(2026, 10, 16), ToDate = new(2026, 10, 16), PageSize = 10 });
            Assert.True(managerB.Succeeded, managerB.Message); Assert.Single(managerB.Value!.Rows.Items);
            var oldManagerLater = await ReadService(db, f, manager: true).GetManagerTeamAsync(new() { FromDate = new(2026, 10, 16), ToDate = new(2026, 10, 16), PageSize = 10 });
            Assert.True(oldManagerLater.Succeeded, oldManagerLater.Message); Assert.Empty(oldManagerLater.Value!.Rows.Items);
        });
    }

    private static AttendanceReadService ReadService(HrmsDbContext db, MySqlLeaveLifecycleIntegrationTests.Fixture f, bool manager = false, TestTenantContext? context = null)
    {
        var tenant = context ?? (manager ? f.ManagerTenant : f.EmployeeTenant);
        var employment = new EffectiveEmploymentResolver(db, tenant);
        var roster = new AttendanceFoundationService(db, tenant, employment, new WorkingDayCalendarResolver(db, tenant, employment));
        return new(db, tenant, roster, new EmployeeIdentityResolver(db, tenant), new EmployeeManagerResolver(db, tenant));
    }

    private static AttendanceDayProcessor Processor(HrmsDbContext db, MySqlLeaveLifecycleIntegrationTests.Fixture f)
    {
        var tenant = f.EmployeeTenant; var employment = new EffectiveEmploymentResolver(db, tenant);
        var roster = new AttendanceFoundationService(db, tenant, employment, new WorkingDayCalendarResolver(db, tenant, employment));
        return new(db, tenant, roster, TimeProvider.System);
    }

    private static Shift AddShift(Guid tenantId, string prefix) => new() { Id = Guid.NewGuid(), TenantId = tenantId, ShiftCode = $"{prefix}-{Guid.NewGuid():N}"[..12], ShiftName = prefix, IsDefault = true, EffectiveFrom = new(2026, 1, 1), StartTime = new(9, 0), EndTime = new(18, 0), PlannedDurationMinutes = 540, FullDayWorkMinutes = 480, MinimumWorkMinutes = 1 };
    private static AttendancePunch Punch(MySqlLeaveLifecycleIntegrationTests.Fixture f, DateTime at, PunchDirection direction) => new() { Id = Guid.NewGuid(), TenantId = f.TenantId, EmployeeId = f.EmployeeId, PunchAtUtc = DateTime.SpecifyKind(at, DateTimeKind.Utc), BusinessDate = new(at.Year, at.Month, at.Day), Direction = direction, Source = PunchSource.Biometric, ExternalPunchId = $"calendar-{Guid.NewGuid():N}", CapturedAtUtc = DateTime.UtcNow };
    private static EmployeeAttendanceDay Day(MySqlLeaveLifecycleIntegrationTests.Fixture f, DateOnly date, EmployeeAttendanceDayStatus status, bool late = false, bool early = false, bool grace = false, bool missingOut = false, bool invalid = false, bool conflict = false, Guid? shiftId = null, RosterDayType dayType = RosterDayType.Shift) => new() { Id = Guid.NewGuid(), TenantId = f.TenantId, EmployeeId = f.EmployeeId, BusinessDate = date, Status = status, RosterDayType = dayType, RosterAssignmentSource = RosterAssignmentSource.System, ShiftId = shiftId, PunchCount = status == EmployeeAttendanceDayStatus.Present ? 2 : 0, SessionCount = status == EmployeeAttendanceDayStatus.Present ? 1 : 0, IsLateIn = late, IsEarlyOut = early, IsGraceApplied = grace, HasMissingOutPunch = missingOut, HasInvalidPunchSequence = invalid, LeaveConflict = conflict, ProcessedAtUtc = DateTime.UtcNow };

    private static async Task WithFixture(Func<MySqlLeaveLifecycleIntegrationTests.Fixture, Task> action)
    {
        var connection = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) throw SkipException.ForSkip("MySQL Phase 3 calendar tests not executed: connection is absent.");
        var fixture = new MySqlLeaveLifecycleIntegrationTests.Fixture(connection);
        try { await fixture.SeedAsync(); await action(fixture); }
        finally { await fixture.CleanupAttendanceAsync(); await fixture.CleanupAsync(); }
    }
}
