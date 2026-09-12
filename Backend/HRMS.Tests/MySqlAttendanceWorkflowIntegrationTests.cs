using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit.Sdk;

namespace HRMS.Tests;

[Collection("Attendance MySQL")]
public sealed class MySqlAttendanceWorkflowIntegrationTests
{
    [Fact]
    public async Task Real_mysql_regularization_approval_persists_adjustment_and_reprocesses_day()
    {
        await WithFixture(async f =>
        {
            await using var db = f.CreateContext();
            var managerId = await AddManagerAsync(db, f);
            var shift = AddShift(f.TenantId, "WF-REG");
            db.Shifts.Add(shift);
            db.AttendancePunches.Add(new AttendancePunch { Id = Guid.NewGuid(), TenantId = f.TenantId, EmployeeId = f.EmployeeId, PunchAtUtc = Utc(2026, 10, 10, 9), BusinessDate = new(2026, 10, 10), Direction = PunchDirection.In, Source = PunchSource.Biometric, ExternalPunchId = $"mysql-wf-{Guid.NewGuid():N}", CapturedAtUtc = DateTime.UtcNow });
            var userId = Guid.NewGuid();
            db.Users.Add(new User { Id = userId, TenantId = f.TenantId, Email = $"wf-{userId:N}@example.test", FirstName = "Workflow", LastName = "User", PasswordHash = "test" });
            await db.SaveChangesAsync();
            var identity = new StubIdentity(f.TenantId, userId, f.EmployeeId);
            var request = (await Service(db, f, identity, managerId).SubmitRegularizationAsync(new(new(2026, 10, 10), AttendanceRegularizationType.MissingOutPunch, null, Utc(2026, 10, 10, 18), "forgot out"))).Value!;
            identity.EmployeeId = managerId;

            var approved = await Service(db, f, identity, managerId).ApproveRegularizationAsync(request.Id);

            Assert.True(approved.Succeeded, approved.Message);
            Assert.Single(await db.AttendanceAdjustments.Where(x => x.AttendanceRegularizationRequestId == request.Id).ToListAsync());
            Assert.Equal(2, await db.AttendanceRegularizationEvents.CountAsync(x => x.AttendanceRegularizationRequestId == request.Id));
            Assert.Single(await db.AttendancePunches.Where(x => x.EmployeeId == f.EmployeeId && x.BusinessDate == new DateOnly(2026, 10, 10)).ToListAsync());
            Assert.Equal(EmployeeAttendanceDayStatus.Present, (await db.EmployeeAttendanceDays.SingleAsync(x => x.EmployeeId == f.EmployeeId && x.BusinessDate == new DateOnly(2026, 10, 10))).Status);
        });
    }

    [Fact]
    public async Task Real_mysql_on_duty_approval_projects_on_duty_and_replay_is_safe()
    {
        await WithFixture(async f =>
        {
            await using var db = f.CreateContext();
            var managerId = await AddManagerAsync(db, f);
            var shift = AddShift(f.TenantId, "WF-OD"); db.Shifts.Add(shift);
            var userId = Guid.NewGuid(); db.Users.Add(new User { Id = userId, TenantId = f.TenantId, Email = $"wf-{userId:N}@example.test", FirstName = "Workflow", LastName = "User", PasswordHash = "test" }); await db.SaveChangesAsync();
            var identity = new StubIdentity(f.TenantId, userId, f.EmployeeId);
            var request = (await Service(db, f, identity, managerId).SubmitOnDutyAsync(new(new(2026, 10, 11), new(2026, 10, 12), "Client work"))).Value!;
            identity.EmployeeId = managerId;

            var service = Service(db, f, identity, managerId);
            var approved = await service.ApproveOnDutyAsync(request.Id);
            var replay = await service.ApproveOnDutyAsync(request.Id);

            Assert.True(approved.Succeeded, approved.Message);
            Assert.Equal(ResultStatus.Conflict, replay.Status);
            Assert.Equal(2, await db.AttendanceOnDutyEvents.CountAsync(x => x.AttendanceOnDutyRequestId == request.Id));
            Assert.Equal(2, await db.EmployeeAttendanceDays.CountAsync(x => x.EmployeeId == f.EmployeeId && x.BusinessDate >= new DateOnly(2026, 10, 11) && x.BusinessDate <= new DateOnly(2026, 10, 12)));
            Assert.All(await db.EmployeeAttendanceDays.Where(x => x.EmployeeId == f.EmployeeId && x.BusinessDate >= new DateOnly(2026, 10, 11) && x.BusinessDate <= new DateOnly(2026, 10, 12)).ToListAsync(), x => Assert.Equal(EmployeeAttendanceDayStatus.OnDuty, x.Status));
        });
    }

    [Fact]
    public async Task Real_mysql_wrong_manager_cannot_approve_workflows()
    {
        await WithFixture(async f =>
        {
            await using var db = f.CreateContext();
            var managerId = await AddManagerAsync(db, f);
            var userId = Guid.NewGuid();
            db.Users.Add(new User { Id = userId, TenantId = f.TenantId, Email = $"wf-{userId:N}@example.test", FirstName = "Workflow", LastName = "User", PasswordHash = "test" });
            await db.SaveChangesAsync();
            var identity = new StubIdentity(f.TenantId, userId, f.EmployeeId);
            var regularization = (await Service(db, f, identity, managerId).SubmitRegularizationAsync(new(new(2026, 10, 13), AttendanceRegularizationType.MissingOutPunch, null, Utc(2026, 10, 13, 18), "forgot out"))).Value!;
            var onDuty = (await Service(db, f, identity, managerId).SubmitOnDutyAsync(new(new(2026, 10, 14), new(2026, 10, 14), "Client work"))).Value!;
            identity.EmployeeId = Guid.NewGuid();

            var service = Service(db, f, identity, managerId);
            Assert.Equal(ResultStatus.Forbidden, (await service.ApproveRegularizationAsync(regularization.Id)).Status);
            Assert.Equal(ResultStatus.Forbidden, (await service.ApproveOnDutyAsync(onDuty.Id)).Status);
            Assert.Equal(AttendanceRequestStatus.Pending, await db.AttendanceRegularizationRequests.Where(x => x.Id == regularization.Id).Select(x => x.Status).SingleAsync());
            Assert.Equal(AttendanceRequestStatus.Pending, await db.AttendanceOnDutyRequests.Where(x => x.Id == onDuty.Id).Select(x => x.Status).SingleAsync());
            Assert.Equal(1, await db.AttendanceRegularizationEvents.CountAsync(x => x.AttendanceRegularizationRequestId == regularization.Id));
            Assert.Equal(1, await db.AttendanceOnDutyEvents.CountAsync(x => x.AttendanceOnDutyRequestId == onDuty.Id));
            Assert.Empty(await db.AttendanceAdjustments.Where(x => x.AttendanceRegularizationRequestId == regularization.Id).ToListAsync());
            Assert.Empty(await db.EmployeeAttendanceDays.Where(x => x.EmployeeId == f.EmployeeId && x.BusinessDate == new DateOnly(2026, 10, 14)).ToListAsync());
        });
    }

    [Fact]
    public async Task Real_mysql_cross_tenant_workflow_mutations_have_no_side_effects()
    {
        await WithFixture(async f =>
        {
            await using var db = f.CreateContext();
            var managerId = await AddManagerAsync(db, f);
            var userId = Guid.NewGuid();
            db.Users.Add(new User { Id = userId, TenantId = f.TenantId, Email = $"wf-{userId:N}@example.test", FirstName = "Workflow", LastName = "User", PasswordHash = "test" });
            await db.SaveChangesAsync();
            var identity = new StubIdentity(f.TenantId, userId, f.EmployeeId);
            var regularization = (await Service(db, f, identity, managerId).SubmitRegularizationAsync(new(new(2026, 10, 15), AttendanceRegularizationType.MissingOutPunch, null, Utc(2026, 10, 15, 18), "forgot out"))).Value!;
            var onDuty = (await Service(db, f, identity, managerId).SubmitOnDutyAsync(new(new(2026, 10, 16), new(2026, 10, 16), "Client work"))).Value!;
            await using var otherDb = f.CreateContext(new TestTenantContext(f.OtherTenantId));
            var otherIdentity = new StubIdentity(f.OtherTenantId, Guid.NewGuid(), f.EmployeeId);
            var otherService = Service(otherDb, f, otherIdentity, managerId);

            Assert.Equal(ResultStatus.NotFound, (await otherService.ApproveRegularizationAsync(regularization.Id)).Status);
            Assert.Equal(ResultStatus.NotFound, (await otherService.ApproveOnDutyAsync(onDuty.Id)).Status);
            Assert.Equal(AttendanceRequestStatus.Pending, await db.AttendanceRegularizationRequests.Where(x => x.Id == regularization.Id).Select(x => x.Status).SingleAsync());
            Assert.Equal(AttendanceRequestStatus.Pending, await db.AttendanceOnDutyRequests.Where(x => x.Id == onDuty.Id).Select(x => x.Status).SingleAsync());
            Assert.Equal(1, await db.AttendanceRegularizationEvents.CountAsync(x => x.AttendanceRegularizationRequestId == regularization.Id));
            Assert.Equal(1, await db.AttendanceOnDutyEvents.CountAsync(x => x.AttendanceOnDutyRequestId == onDuty.Id));
            Assert.Empty(await db.AttendanceAdjustments.Where(x => x.AttendanceRegularizationRequestId == regularization.Id).ToListAsync());
            Assert.Empty(await db.EmployeeAttendanceDays.Where(x => x.EmployeeId == f.EmployeeId && x.BusinessDate == new DateOnly(2026, 10, 16)).ToListAsync());
        });
    }

    private static AttendanceWorkflowService Service(HRMS.Infrastructure.Persistence.HrmsDbContext db, MySqlLeaveLifecycleIntegrationTests.Fixture f, StubIdentity identity, Guid managerId)
    {
        var tenant = f.EmployeeTenant;
        var employment = new EffectiveEmploymentResolver(db, tenant);
        var roster = new AttendanceFoundationService(db, tenant, employment, new WorkingDayCalendarResolver(db, tenant, employment));
        var processor = new AttendanceDayProcessor(db, tenant, roster, new FixedClock(new DateTimeOffset(2026, 10, 20, 20, 0, 0, TimeSpan.Zero)));
        return new(db, identity, new FixedManagerResolver(managerId), processor, new FixedClock(new DateTimeOffset(2026, 10, 20, 20, 0, 0, TimeSpan.Zero)));
    }

    private static async Task<Guid> AddManagerAsync(HRMS.Infrastructure.Persistence.HrmsDbContext db, MySqlLeaveLifecycleIntegrationTests.Fixture f)
    {
        var id = Guid.NewGuid();
        db.Employees.Add(new Employee { Id = id, TenantId = f.TenantId, EmployeeCode = $"M{Guid.NewGuid():N}"[..10], FirstName = "Manager", LastName = "Workflow", DateOfJoining = new(2026, 1, 1) });
        db.EmployeeEmploymentHistory.Add(new EmployeeEmploymentHistory { Id = Guid.NewGuid(), TenantId = f.TenantId, EmployeeId = id, EffectiveFrom = new(2026, 1, 1), EmploymentStatus = EmployeeStatus.Active });
        await db.SaveChangesAsync();
        return id;
    }

    private static Shift AddShift(Guid tenantId, string code) => new() { Id = Guid.NewGuid(), TenantId = tenantId, ShiftCode = $"{code}-{Guid.NewGuid():N}"[..Math.Min(25, code.Length + 9)], ShiftName = code, IsDefault = true, IsActive = true, EffectiveFrom = new(2026, 1, 1), StartTime = new(9, 0), EndTime = new(18, 0), PlannedDurationMinutes = 540, FullDayWorkMinutes = 480, MinimumWorkMinutes = 1, CaptureMode = AttendanceCaptureMode.Mixed };
    private static DateTime Utc(int year, int month, int day, int hour) => new(year, month, day, hour, 0, 0, DateTimeKind.Utc);

    private static async Task WithFixture(Func<MySqlLeaveLifecycleIntegrationTests.Fixture, Task> action)
    {
        var connection = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) throw SkipException.ForSkip("MySQL Phase 4 workflow tests not executed: connection is absent.");
        var f = new MySqlLeaveLifecycleIntegrationTests.Fixture(connection);
        try { await f.SeedAsync(); await action(f); }
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

    private sealed class StubIdentity(Guid tenantId, Guid userId, Guid employeeId) : IEmployeeIdentityResolver
    {
        public Guid EmployeeId { get; set; } = employeeId;
        public Task<Result<RuntimeEmployeeIdentity>> ResolveCurrentAsync(CancellationToken cancellationToken = default) => Task.FromResult(Result<RuntimeEmployeeIdentity>.Success(new(tenantId, userId, EmployeeId)));
    }

    private sealed class FixedManagerResolver(Guid managerId) : IEmployeeManagerResolver
    {
        public Task<Result<EmployeeManagerResolution>> ResolveAsync(Guid employeeId, DateOnly asOfDate, CancellationToken cancellationToken = default) => Task.FromResult(Result<EmployeeManagerResolution>.Success(new(EmployeeManagerResolutionStatus.Resolved, employeeId, managerId, "MGR", "Manager", "mysql")));
        public Task<bool> WouldCreateCycleAsync(Guid employeeId, Guid proposedManagerId, DateOnly asOfDate, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }
}
