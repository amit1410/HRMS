using System.Data.Common;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MySql.Data.MySqlClient;
using Xunit.Sdk;

namespace HRMS.Tests;

[Collection("Attendance MySQL")]
public sealed class MySqlAttendancePhase4ConcurrencyTests
{
    [Fact]
    public async Task Regularization_concurrent_approvals_allow_one_winner_on_mysql() => await RunRegularizationRace(false);

    [Fact]
    public async Task Regularization_approve_reject_race_allows_one_terminal_transition_on_mysql() => await RunRegularizationRace(true);

    [Fact]
    public async Task OnDuty_concurrent_approvals_allow_one_winner_on_mysql() => await RunOnDutyRace(false);

    [Fact]
    public async Task OnDuty_approve_reject_race_allows_one_terminal_transition_on_mysql() => await RunOnDutyRace(true);

    private static async Task RunRegularizationRace(bool rejectSecond)
    {
        await WithFixture(async f =>
        {
            var date = new DateOnly(2026, 10, 10);
            await AddWorkingFixtureAsync(f, date);
            await using var employeeDb = f.CreateContext(f.EmployeeTenant);
            var employeeIdentity = new TestIdentity(f.TenantId, f.EmployeeUserId, f.EmployeeId);
            var request = (await Service(employeeDb, f, employeeIdentity).SubmitRegularizationAsync(new(date, AttendanceRegularizationType.MissingOutPunch, null, Utc(2026, 10, 10, 18), "forgot out"))).Value!;
            var barrier = new TerminalUpdateBarrier("AttendanceRegularizationRequests");
            await using var dbA = f.CreateContextWithInterceptors(f.ManagerTenant, barrier);
            await using var dbB = f.CreateContextWithInterceptors(f.ManagerTenant, barrier);
            Assert.Equal(AttendanceRequestStatus.Pending, (await dbA.AttendanceRegularizationRequests.SingleAsync(x => x.Id == request.Id)).Status);
            Assert.Equal(AttendanceRequestStatus.Pending, (await dbB.AttendanceRegularizationRequests.SingleAsync(x => x.Id == request.Id)).Status);

            var managerIdentityA = new TestIdentity(f.TenantId, f.ManagerUserId, f.ManagerId);
            var managerIdentityB = new TestIdentity(f.TenantId, f.ManagerUserId, f.ManagerId);
            var first = Service(dbA, f, managerIdentityA);
            var second = Service(dbB, f, managerIdentityB);
            var calls = await Task.WhenAll(
                first.ApproveRegularizationAsync(request.Id),
                rejectSecond ? second.RejectRegularizationAsync(request.Id, "Not approved") : second.ApproveRegularizationAsync(request.Id)).WaitAsync(TimeSpan.FromSeconds(30));

            Assert.Equal(2, barrier.ReachedCount);
            Assert.Single(calls, x => x.Succeeded);
            Assert.Single(calls, x => x.Status == ResultStatus.Conflict);
            await using var verify = f.CreateContext(new TestTenantContext(f.TenantId));
            var persisted = await verify.AttendanceRegularizationRequests.SingleAsync(x => x.Id == request.Id);
            Assert.Equal(calls.Single(x => x.Succeeded).Value!.Status, persisted.Status);
            Assert.Equal(1, await verify.AttendanceRegularizationEvents.CountAsync(x => x.AttendanceRegularizationRequestId == request.Id && (x.EventType == AttendanceRequestEventType.Approved || x.EventType == AttendanceRequestEventType.Rejected)));
            Assert.Equal(persisted.Status == AttendanceRequestStatus.Approved ? 1 : 0, await verify.AttendanceAdjustments.CountAsync(x => x.AttendanceRegularizationRequestId == request.Id));
            Assert.Single(await verify.EmployeeAttendanceDays.Where(x => x.EmployeeId == f.EmployeeId && x.BusinessDate == date).ToListAsync());
        });
    }

    private static async Task RunOnDutyRace(bool rejectSecond)
    {
        await WithFixture(async f =>
        {
            var date = new DateOnly(2026, 10, 11);
            await AddWorkingFixtureAsync(f, date);
            await using var employeeDb = f.CreateContext(f.EmployeeTenant);
            var employeeIdentity = new TestIdentity(f.TenantId, f.EmployeeUserId, f.EmployeeId);
            var request = (await Service(employeeDb, f, employeeIdentity).SubmitOnDutyAsync(new(date, date, "Client work"))).Value!;
            var barrier = new TerminalUpdateBarrier("AttendanceOnDutyRequests");
            await using var dbA = f.CreateContextWithInterceptors(f.ManagerTenant, barrier);
            await using var dbB = f.CreateContextWithInterceptors(f.ManagerTenant, barrier);
            Assert.Equal(AttendanceRequestStatus.Pending, (await dbA.AttendanceOnDutyRequests.SingleAsync(x => x.Id == request.Id)).Status);
            Assert.Equal(AttendanceRequestStatus.Pending, (await dbB.AttendanceOnDutyRequests.SingleAsync(x => x.Id == request.Id)).Status);

            var calls = await Task.WhenAll(
                Service(dbA, f, new TestIdentity(f.TenantId, f.ManagerUserId, f.ManagerId)).ApproveOnDutyAsync(request.Id),
                rejectSecond ? Service(dbB, f, new TestIdentity(f.TenantId, f.ManagerUserId, f.ManagerId)).RejectOnDutyAsync(request.Id, "Not approved") : Service(dbB, f, new TestIdentity(f.TenantId, f.ManagerUserId, f.ManagerId)).ApproveOnDutyAsync(request.Id)).WaitAsync(TimeSpan.FromSeconds(30));

            Assert.Equal(2, barrier.ReachedCount);
            Assert.Single(calls, x => x.Succeeded);
            Assert.Single(calls, x => x.Status == ResultStatus.Conflict);
            await using var verify = f.CreateContext(new TestTenantContext(f.TenantId));
            var persisted = await verify.AttendanceOnDutyRequests.SingleAsync(x => x.Id == request.Id);
            Assert.Equal(calls.Single(x => x.Succeeded).Value!.Status, persisted.Status);
            Assert.Equal(1, await verify.AttendanceOnDutyEvents.CountAsync(x => x.AttendanceOnDutyRequestId == request.Id && (x.EventType == AttendanceRequestEventType.Approved || x.EventType == AttendanceRequestEventType.Rejected)));
            Assert.Equal(persisted.Status == AttendanceRequestStatus.Approved ? 1 : 0, await verify.EmployeeAttendanceDays.CountAsync(x => x.EmployeeId == f.EmployeeId && x.BusinessDate == date && x.Status == EmployeeAttendanceDayStatus.OnDuty));
        });
    }

    private static async Task AddWorkingFixtureAsync(MySqlLeaveLifecycleIntegrationTests.Fixture f, DateOnly date)
    {
        await using var db = f.CreateContext(f.EmployeeTenant);
        var shift = new Shift { Id = Guid.NewGuid(), TenantId = f.TenantId, ShiftCode = $"R{Guid.NewGuid():N}"[..10], ShiftName = "Race Shift", IsDefault = true, IsActive = true, EffectiveFrom = new(2026, 1, 1), StartTime = new(9, 0), EndTime = new(18, 0), PlannedDurationMinutes = 540, FullDayWorkMinutes = 480, MinimumWorkMinutes = 1, CaptureMode = AttendanceCaptureMode.Mixed };
        db.Shifts.Add(shift);
        db.EmployeeAttendanceDays.Add(new EmployeeAttendanceDay { Id = Guid.NewGuid(), TenantId = f.TenantId, EmployeeId = f.EmployeeId, BusinessDate = date, ShiftId = shift.Id, ShiftCode = shift.ShiftCode, ExpectedWorkMinutes = 480, RosterAssignmentSource = RosterAssignmentSource.Auto, RosterDayType = RosterDayType.Shift, Status = EmployeeAttendanceDayStatus.Incomplete, HasMissingOutPunch = true });
        await db.SaveChangesAsync();
    }

    private static AttendanceWorkflowService Service(HrmsDbContext db, MySqlLeaveLifecycleIntegrationTests.Fixture f, TestIdentity identity) => new(db, identity, new FixedManagerResolver(f.ManagerId), new AttendanceDayProcessor(db, f.ManagerTenant, new AttendanceFoundationService(db, f.ManagerTenant, new EffectiveEmploymentResolver(db, f.ManagerTenant))), new FixedClock(new DateTimeOffset(2026, 10, 20, 20, 0, 0, TimeSpan.Zero)));
    private static DateTime Utc(int year, int month, int day, int hour) => new(year, month, day, hour, 0, 0, DateTimeKind.Utc);

    private static async Task WithFixture(Func<MySqlLeaveLifecycleIntegrationTests.Fixture, Task> action)
    {
        var connection = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) throw SkipException.ForSkip("MySQL Phase 4 race tests not executed: connection is absent.");
        var f = new MySqlLeaveLifecycleIntegrationTests.Fixture(connection);
        try
        {
            await f.SeedAsync();
            await using (var schema = f.CreateContext(new TestTenantContext()))
            {
                // Existing shared test databases may predate the revision migration.
                // Keep this compatibility guard idempotent while the migration remains
                // the production schema authority.
                await AddConcurrencyColumnAsync(schema, "AttendanceRegularizationRequests");
                await AddConcurrencyColumnAsync(schema, "AttendanceOnDutyRequests");
            }
            await action(f);
        }
        finally
        {
            await using var cleanup = f.CreateContext(new TestTenantContext());
            await cleanup.Database.ExecuteSqlRawAsync("DELETE FROM `AttendanceAdjustments`");
            await cleanup.Database.ExecuteSqlRawAsync("DELETE FROM `AttendanceRegularizationEvents`");
            await cleanup.Database.ExecuteSqlRawAsync("DELETE FROM `AttendanceRegularizationRequests`");
            await cleanup.Database.ExecuteSqlRawAsync("DELETE FROM `AttendanceOnDutyEvents`");
            await cleanup.Database.ExecuteSqlRawAsync("DELETE FROM `AttendanceOnDutyRequests`");
            await f.CleanupAttendanceAsync(); await f.CleanupAsync();
        }
    }

    private static async Task AddConcurrencyColumnAsync(HrmsDbContext db, string table)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync($"ALTER TABLE `{table}` ADD COLUMN `ConcurrencyVersion` int NOT NULL DEFAULT 1");
        }
        catch (MySqlException ex) when (ex.Number == 1060)
        {
            // The migration has already applied the column in this shared test database.
        }
    }

    private sealed class TestIdentity(Guid tenantId, Guid userId, Guid employeeId) : IEmployeeIdentityResolver
    {
        public Task<Result<RuntimeEmployeeIdentity>> ResolveCurrentAsync(CancellationToken cancellationToken = default) => Task.FromResult(Result<RuntimeEmployeeIdentity>.Success(new(tenantId, userId, employeeId)));
    }

    private sealed class FixedManagerResolver(Guid managerId) : IEmployeeManagerResolver
    {
        public Task<Result<EmployeeManagerResolution>> ResolveAsync(Guid employeeId, DateOnly asOfDate, CancellationToken cancellationToken = default) => Task.FromResult(Result<EmployeeManagerResolution>.Success(new(EmployeeManagerResolutionStatus.Resolved, employeeId, managerId, "MGR", "Manager", "mysql-race")));
        public Task<bool> WouldCreateCycleAsync(Guid employeeId, Guid proposedManagerId, DateOnly asOfDate, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class TerminalUpdateBarrier(string table) : DbCommandInterceptor
    {
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int reachedCount;
        public int ReachedCount => Volatile.Read(ref reachedCount);

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains($"UPDATE `{table}`", StringComparison.OrdinalIgnoreCase))
            {
                if (Interlocked.Increment(ref reachedCount) == 2) release.TrySetResult();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            }
            return await base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
