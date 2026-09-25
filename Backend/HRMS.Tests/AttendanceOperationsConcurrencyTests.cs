using HRMS.Application.Abstractions;
using HRMS.Application.Services;
using HRMS.Domain.Authorization;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;

namespace HRMS.Tests;

/// <summary>
/// Races the real Attendance workflow/finalization services over independent EF contexts
/// sharing the test's named SQLite in-memory database. This is SQLite concurrency evidence only.
/// </summary>
public sealed class AttendanceOperationsConcurrencyTests
{
    private static readonly DateOnly WorkDate = new(2026, 9, 10);

    [Fact]
    public async Task Correction_vs_correction_same_day()
    {
        using var f = await AttendanceWorkflowTests.FixtureAsync();
        await AttendanceWorkflowTests.SeedIncompleteDayAsync(f, WorkDate);
        var requestInput = new ManualAttendanceRequest(f.EmployeeId, WorkDate, AttendanceRegularizationType.CorrectInOutTime, WorkDate.ToDateTime(new(9, 0), DateTimeKind.Utc), WorkDate.ToDateTime(new(18, 0), DateTimeKind.Utc), "concurrent correction", null, 1);
        async Task<HRMS.Application.Common.Result<RegularizationDto>> Submit(HRMS.Infrastructure.Persistence.HrmsDbContext db, TestTenantContext tenant, ManualAttendanceRequest input, IEmployeeSerializationLock employeeLock)
        {
            return await new AttendanceOperationsService(db, tenant, employeeLock: employeeLock).SubmitManualAttendanceAsync(input);
        }
        var coordinator = new SqliteEmployeeSerializationCoordinator();
        using var barrier = new Barrier(2);
        async Task<HRMS.Application.Common.Result<RegularizationDto>> Run(ManualAttendanceRequest input)
        {
            await using var db = f.CreateIsolatedContext(f.Identity.UserId, out var tenant, coordinator);
            barrier.SignalAndWait(TimeSpan.FromSeconds(10));
            return await Submit(db, tenant, input, coordinator);
        }
        var outcomes = await Task.WhenAll(Task.Run(() => Run(requestInput)), Task.Run(() => Run(requestInput with { ProposedInAtUtc = WorkDate.ToDateTime(new(9, 15), DateTimeKind.Utc), Reason = "competing correction" })));
        Assert.Single(outcomes.Where(x => x.Succeeded));
        var winner = outcomes.Single(x => x.Succeeded).Value!;
        await using (var checkerDb = f.CreateIsolatedContext(f.ManagerUserId, out _))
        {
            var approved = await f.CreateService(checkerDb, new TestTenantContext(f.TenantId, f.ManagerUserId), f.ManagerUserId, f.ManagerId).ApproveRegularizationAsync(winner.Id);
            Assert.True(approved.Succeeded, approved.Message);
        }
        await using var verify = f.CreateIsolatedContext(f.Identity.UserId, out _);
        Assert.Equal(AttendanceRequestStatus.Approved, await verify.AttendanceRegularizationRequests.Where(x => x.Id == winner.Id).Select(x => x.Status).SingleAsync());
        Assert.Equal(1, await verify.AttendanceRegularizationRequests.CountAsync(x => x.BusinessDate == WorkDate));
        var adjustment = await verify.AttendanceAdjustments.SingleAsync(x => x.AttendanceRegularizationRequestId == winner.Id);
        Assert.Equal(winner.ProposedInAtUtc, adjustment.EffectiveInAtUtc);
        Assert.Equal(winner.ProposedOutAtUtc, adjustment.EffectiveOutAtUtc);
        Assert.Equal(EmployeeAttendanceDayStatus.Present, await verify.EmployeeAttendanceDays.Where(x => x.EmployeeId == f.EmployeeId && x.BusinessDate == WorkDate).Select(x => x.Status).SingleAsync());
    }

    [Fact]
    public async Task Approval_vs_approval()
    {
        using var f = await PendingCorrectionAsync();
        var checkerB = Guid.NewGuid();
        f.Context.Users.Add(new User { Id = checkerB, TenantId = f.TenantId, Email = $"{checkerB:N}@example.test", FirstName = "Checker", LastName = "B", PasswordHash = "test", IsActive = true });
        await f.Context.SaveChangesAsync();
        var outcomes = await RaceAsync(f,
            (db, tenant) => f.CreateService(db, tenant, f.ManagerUserId, f.ManagerId).ApproveRegularizationAsync(f.RequestId),
            (db, tenant) => f.CreateService(db, tenant, checkerB, f.ManagerId).ApproveRegularizationAsync(f.RequestId));
        Assert.Single(outcomes.Where(x => x.Succeeded));
        await using var verify = f.CreateIsolatedContext(f.ManagerUserId, out _);
        Assert.Equal(AttendanceRequestStatus.Approved, (await verify.AttendanceRegularizationRequests.SingleAsync(x => x.Id == f.RequestId)).Status);
        Assert.Single(await verify.AttendanceAdjustments.Where(x => x.AttendanceRegularizationRequestId == f.RequestId).ToListAsync());
        Assert.Single(await verify.AttendanceRegularizationEvents.Where(x => x.AttendanceRegularizationRequestId == f.RequestId && x.EventType == AttendanceRequestEventType.Approved).ToListAsync());
    }

    [Fact]
    public async Task Approval_vs_rejection()
    {
        using var f = await PendingCorrectionAsync();
        var outcomes = await RaceAsync(f,
            (db, tenant) => f.CreateService(db, tenant, f.ManagerUserId, f.ManagerId).ApproveRegularizationAsync(f.RequestId),
            (db, tenant) => f.CreateService(db, tenant, f.ManagerUserId, f.ManagerId).RejectRegularizationAsync(f.RequestId, "concurrent rejection"));
        Assert.Single(outcomes.Where(x => x.Succeeded));
        await using var verify = f.CreateIsolatedContext(f.ManagerUserId, out _);
        var request = await verify.AttendanceRegularizationRequests.SingleAsync(x => x.Id == f.RequestId);
        Assert.Contains(request.Status, new[] { AttendanceRequestStatus.Approved, AttendanceRequestStatus.Rejected });
        var adjustments = await verify.AttendanceAdjustments.CountAsync(x => x.AttendanceRegularizationRequestId == f.RequestId);
        Assert.Equal(request.Status == AttendanceRequestStatus.Approved ? 1 : 0, adjustments);
    }

    [Fact]
    public async Task Period_finalize_vs_correction()
    {
        using var f = await PendingCorrectionAsync();
        await using var seed = f.CreateIsolatedContext(f.ManagerUserId, out var seedTenant);
        var monthly = new AttendanceMonthlyProcessor(seed, seedTenant);
        var period = (await monthly.CreatePeriodAsync(new(2026, 9))).Value!;
        Assert.True((await monthly.ProcessAsync(period.Id)).Succeeded);
        var results = await RaceAsync<object>(f,
            async (db, tenant) => await new AttendanceMonthlyProcessor(db, tenant).CloseAsync(period.Id),
            async (db, tenant) => await f.CreateService(db, tenant, f.ManagerUserId, f.ManagerId).ApproveRegularizationAsync(f.RequestId));
        await using var verify = f.CreateIsolatedContext(f.ManagerUserId, out _);
        var persistedPeriod = await verify.AttendancePeriods.SingleAsync(x => x.Id == period.Id);
        var persistedRequest = await verify.AttendanceRegularizationRequests.SingleAsync(x => x.Id == f.RequestId);
        var closeResult = (HRMS.Application.Common.Result<AttendancePeriodDto>)results.Single(x => x is HRMS.Application.Common.Result<AttendancePeriodDto>);
        var correctionResult = (HRMS.Application.Common.Result<RegularizationDto>)results.Single(x => x is HRMS.Application.Common.Result<RegularizationDto>);
        if (closeResult.Succeeded)
        {
            Assert.NotEqual(AttendanceRequestStatus.Approved, persistedRequest.Status);
            Assert.Single(await verify.PayrollAttendanceSnapshots.Where(x => x.AttendancePeriodId == period.Id && x.IsCurrent).ToListAsync());
        }
        else
        {
            Assert.True(correctionResult.Succeeded, correctionResult.Message);
            Assert.NotEqual(AttendancePeriodStatus.Closed, persistedPeriod.Status);
        }
        Assert.True(await verify.PayrollAttendanceSnapshots.CountAsync(x => x.AttendancePeriodId == period.Id && x.IsCurrent) <= 1);
    }

    [Fact]
    public async Task Period_reopen_vs_payroll_resolution()
    {
        await using var f = await Phase6BAcceptanceData.FinalizedAsync();
        var snapshotId = await f.Db.PayrollAttendanceSnapshots.Where(x => x.AttendancePeriodId == f.PeriodId && x.IsCurrent).Select(x => x.Id).SingleAsync();
        using var barrier = new Barrier(2);
        var reopen = Task.Run(async () => { await using var db = f.CreateIsolatedContext(Guid.NewGuid()); barrier.SignalAndWait(); return await new AttendanceMonthlyProcessor(db, new TestTenantContext(f.TenantId)).ReopenAsync(f.PeriodId, new("controlled concurrent reopen")); });
        var payroll = Task.Run(async () => { await using var db = f.CreateIsolatedContext(Guid.NewGuid()); barrier.SignalAndWait(); return await new AttendancePayrollSnapshotResolver(db, new TestTenantContext(f.TenantId)).ResolveAsync(f.EmployeeId, new(2026, 9, 1), new(2026, 9, 30)); });
        await Task.WhenAll(reopen, payroll);
        Assert.True(reopen.Result.Succeeded, reopen.Result.Message);
        Assert.True(payroll.Result.Succeeded || payroll.Result.Message.Contains("AttendanceNotFinalized", StringComparison.Ordinal), payroll.Result.Message);
        if (payroll.Result.Succeeded)
        {
            Assert.Equal(snapshotId, payroll.Result.Value!.SnapshotId);
            Assert.Equal(1, payroll.Result.Value.Version);
        }
        await using var verify = f.CreateIsolatedContext(Guid.NewGuid());
        var historicalSnapshot = await verify.PayrollAttendanceSnapshots.SingleAsync(x => x.Id == snapshotId);
        Assert.Equal(1, historicalSnapshot.Version);
        Assert.Single(await verify.PayrollAttendanceSnapshots.Where(x => x.AttendancePeriodId == f.PeriodId && x.IsCurrent).ToListAsync());
    }

    [Fact]
    public async Task Refinalize_vs_refinalize()
    {
        await using var f = await Phase6BAcceptanceData.FinalizedAsync();
        Assert.True((await f.Processor.ReopenAsync(f.PeriodId, new("race setup"))).Succeeded);
        Assert.True((await f.Processor.ProcessAsync(f.PeriodId)).Succeeded);
        using var barrier = new Barrier(2);
        async Task<HRMS.Application.Common.Result<AttendancePeriodDto>> Close()
        {
            await using var db = f.CreateIsolatedContext(Guid.NewGuid());
            barrier.SignalAndWait();
            return await new AttendanceMonthlyProcessor(db, new TestTenantContext(f.TenantId)).CloseAsync(f.PeriodId);
        }
        var results = await Task.WhenAll(Task.Run(Close), Task.Run(Close));
        Assert.Contains(results, x => x.Succeeded);
        await using var verify = f.CreateIsolatedContext(Guid.NewGuid());
        var snapshots = await verify.PayrollAttendanceSnapshots.Where(x => x.AttendancePeriodId == f.PeriodId).ToListAsync();
        Assert.Equal(1, snapshots.Count(x => x.IsCurrent));
        Assert.Contains(snapshots, x => x.Version == 1);
    }

    [Fact]
    public async Task Bulk_update_vs_single_update()
    {
        using var f = await PendingCorrectionAsync();
        var results = await RaceAsync<(bool Bulk, bool Success)>(f,
            async (db, tenant) => { var result = await new AttendanceOperationsService(db, tenant).ApplyBulkActionAsync([new(f.RequestId, false, true, 1, "bulk race")]); return (true, result.Succeeded && result.Value!.Items.Single().Success); },
            async (db, tenant) => { var result = await f.CreateService(db, tenant, f.ManagerUserId, f.ManagerId).ApproveRegularizationAsync(f.RequestId); return (false, result.Succeeded); });
        Assert.Single(results.Where(x => x.Success));
        Assert.Single(results.Where(x => !x.Success));
        await using var verify = f.CreateIsolatedContext(f.ManagerUserId, out _);
        var request = await verify.AttendanceRegularizationRequests.SingleAsync(x => x.Id == f.RequestId);
        Assert.Equal(AttendanceRequestStatus.Approved, request.Status);
        Assert.Single(await verify.AttendanceAdjustments.Where(x => x.AttendanceRegularizationRequestId == f.RequestId).ToListAsync());
    }

    [Fact]
    public async Task Leave_approval_vs_absence_correction()
    {
        using var f = await AttendanceWorkflowTests.FixtureAsync();
        var date = WorkDate;
        var absent = await f.Processor.ProcessAsync(f.EmployeeId, date);
        Assert.True(absent.Succeeded, absent.Message);
        Assert.Equal(EmployeeAttendanceDayStatus.Absent, absent.Value!.Status);
        var leave = await AttendanceLeaveIntegrationTests.AddPendingLeaveAsync(f.Context, f.TenantId, f.EmployeeId, date);
        var employeeUserId = f.Identity.UserId;
        const int roleId = 99631;
        const int permissionId = 99632;
        f.Context.Roles.Add(new Role { Id = roleId, Name = "AttendanceConcurrencyLeaveApprover", Description = "Concurrency test" });
        f.Context.Permissions.Add(new Permission { Id = permissionId, Name = Permissions.Leave.Approve, Description = "Concurrency test" });
        f.Context.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionId = permissionId });
        f.Context.UserRoles.Add(new UserRole { Id = Guid.NewGuid(), TenantId = f.TenantId, UserId = f.ManagerUserId, RoleId = roleId, EffectiveFrom = new(2026, 1, 1) });
        await f.Context.SaveChangesAsync();
        var tenantForMaker = new TestTenantContext(f.TenantId, employeeUserId);
        var correction = await new AttendanceOperationsService(f.Context, tenantForMaker).SubmitManualAttendanceAsync(
            new(f.EmployeeId, date, AttendanceRegularizationType.CorrectInOutTime,
                date.ToDateTime(new(9, 0), DateTimeKind.Utc), date.ToDateTime(new(18, 0), DateTimeKind.Utc),
                "Correct absence with verified attendance", "Concurrent Leave/Attendance race", 1));
        Assert.True(correction.Succeeded, correction.Message);

        using var barrier = new Barrier(2);
        var leaveApproval = Task.Run(async () =>
        {
            await using var db = f.CreateIsolatedContext(f.ManagerUserId, out var tenant);
            var identity = new AttendanceWorkflowTests.MutableIdentity(f.TenantId, employeeUserId, f.EmployeeId, f.ManagerUserId, f.ManagerId) { EmployeeId = f.ManagerId };
            var employment = new EffectiveEmploymentResolver(db, tenant);
            var calendar = new WorkingDayCalendarResolver(db, tenant, employment);
            var processor = new AttendanceDayProcessor(db, tenant, new AttendanceFoundationService(db, tenant, employment, calendar), TimeProvider.System, new AttendancePeriodLockService(db, tenant));
            var service = new LeaveRequestApprovalService(db, identity, f.Managers, new NoopEmployeeSerializationLock(), TimeProvider.System, attendanceProcessor: processor);
            barrier.SignalAndWait(TimeSpan.FromSeconds(10));
            return await service.ApproveAsync(leave.RequestId);
        });
        var attendanceApproval = Task.Run(async () =>
        {
            await using var db = f.CreateIsolatedContext(f.ManagerUserId, out var tenant);
            barrier.SignalAndWait(TimeSpan.FromSeconds(10));
            return await f.CreateService(db, tenant, f.ManagerUserId, f.ManagerId).ApproveRegularizationAsync(correction.Value!.Id);
        });
        await Task.WhenAll(leaveApproval, attendanceApproval);
        Assert.True(leaveApproval.Result.Succeeded, leaveApproval.Result.Message);
        Assert.True(attendanceApproval.Result.Succeeded, attendanceApproval.Result.Message);

        await using var verify = f.CreateIsolatedContext(f.ManagerUserId, out _);
        Assert.Equal(LeaveRequestStatus.Approved, await verify.LeaveRequests.Where(x => x.Id == leave.RequestId).Select(x => x.Status).SingleAsync());
        Assert.Equal(AttendanceRequestStatus.Approved, await verify.AttendanceRegularizationRequests.Where(x => x.Id == correction.Value!.Id).Select(x => x.Status).SingleAsync());
        Assert.InRange(await verify.AttendanceAdjustments.CountAsync(x => x.AttendanceRegularizationRequestId == correction.Value!.Id), 0, 1);
        var finalDay = await verify.EmployeeAttendanceDays.SingleAsync(x => x.EmployeeId == f.EmployeeId && x.BusinessDate == date);
        Assert.NotEqual(EmployeeAttendanceDayStatus.Absent, finalDay.Status);
        var exceptions = await new AttendanceOperationsService(verify, new TestTenantContext(f.TenantId, f.ManagerUserId))
            .GetOperationalExceptionsAsync(new() { FromDate = date, ToDate = date, ExceptionType = AttendanceExceptionType.Absent, Page = 1, PageSize = 10 });
        Assert.Empty(exceptions.Value!.Items);
        Assert.Empty(await verify.AttendanceExceptionResolutions.Where(x => x.EmployeeId == f.EmployeeId && x.ExceptionType == AttendanceExceptionType.Absent).ToListAsync());
    }

    [Fact]
    public async Task Regularization_approval_vs_manual_correction()
    {
        using var f = await PendingCorrectionAsync();
        var employeeScope = new TestTenantContext(f.TenantId, f.Identity.UserId);
        var results = await RaceAsync<(bool IsManual, object Result)>(f,
            async (db, tenant) => (false, await f.CreateService(db, tenant, f.ManagerUserId, f.ManagerId).ApproveRegularizationAsync(f.RequestId)),
            async (db, tenant) => (true, await new AttendanceOperationsService(db, employeeScope).SubmitManualAttendanceAsync(new(f.EmployeeId, WorkDate, AttendanceRegularizationType.CorrectInOutTime, WorkDate.ToDateTime(new(9, 0), DateTimeKind.Utc), WorkDate.ToDateTime(new(18, 0), DateTimeKind.Utc), "manual correction race", null, 1))));
        var approvalResult = (HRMS.Application.Common.Result<RegularizationDto>)results.Single(x => !x.IsManual).Result;
        Assert.True(approvalResult.Succeeded, approvalResult.Message);
        var manualResult = (HRMS.Application.Common.Result<RegularizationDto>)results.Single(x => x.IsManual).Result;
        Assert.True(manualResult.Succeeded || manualResult.Status == HRMS.Application.Common.ResultStatus.Conflict,
            $"Expected either a conflict while the original request is pending or a valid follow-on request after approval; got {manualResult.Status}: {manualResult.Message}");
        await using var verify = f.CreateIsolatedContext(f.ManagerUserId, out _);
        var pending = await verify.AttendanceRegularizationRequests.Where(x => x.EmployeeId == f.EmployeeId && x.BusinessDate == WorkDate && x.Status == AttendanceRequestStatus.Pending).ToListAsync();
        if (manualResult.Succeeded)
        {
            Assert.Single(pending);
            Assert.Equal("manual correction race", pending[0].Reason);
            Assert.Single(await verify.EmployeeAuditLogs.Where(x => x.EmployeeId == f.EmployeeId && x.FieldName == "ManualAttendanceRequested" && x.EffectiveDate == WorkDate).ToListAsync());
        }
        else
        {
            Assert.Empty(pending);
            Assert.Empty(await verify.EmployeeAuditLogs.Where(x => x.EmployeeId == f.EmployeeId && x.FieldName == "ManualAttendanceRequested" && x.EffectiveDate == WorkDate).ToListAsync());
        }
        Assert.Single(await verify.AttendanceAdjustments.Where(x => x.AttendanceRegularizationRequestId == f.RequestId).ToListAsync());
        Assert.Equal(1, await verify.AttendanceRegularizationEvents.CountAsync(x => x.AttendanceRegularizationRequestId == f.RequestId && x.EventType == AttendanceRequestEventType.Approved));
        Assert.NotEqual(EmployeeAttendanceDayStatus.Absent, await verify.EmployeeAttendanceDays.Where(x => x.EmployeeId == f.EmployeeId && x.BusinessDate == WorkDate).Select(x => x.Status).SingleAsync());
    }

    [Fact]
    public async Task Duplicate_audit_event_race()
    {
        using var f = await PendingCorrectionAsync();
        var results = await RaceAsync(f,
            (db, tenant) => f.CreateService(db, tenant, f.ManagerUserId, f.ManagerId).ApproveRegularizationAsync(f.RequestId),
            (db, tenant) => f.CreateService(db, tenant, f.ManagerUserId, f.ManagerId).ApproveRegularizationAsync(f.RequestId));
        Assert.Single(results.Where(x => x.Succeeded));
        await using var verify = f.CreateIsolatedContext(f.ManagerUserId, out _);
        Assert.Single(await verify.AttendanceRegularizationEvents.Where(x => x.AttendanceRegularizationRequestId == f.RequestId && x.EventType == AttendanceRequestEventType.Approved).ToListAsync());
        Assert.Single(await verify.AttendanceAdjustments.Where(x => x.AttendanceRegularizationRequestId == f.RequestId).ToListAsync());
    }

    private static async Task<AttendanceWorkflowTests.WorkflowFixture> PendingCorrectionAsync()
    {
        var f = await AttendanceWorkflowTests.FixtureAsync();
        await AttendanceWorkflowTests.SeedIncompleteDayAsync(f, WorkDate);
        var request = await AttendanceWorkflowTests.Service(f).SubmitRegularizationAsync(new(WorkDate, AttendanceRegularizationType.MissingOutPunch, null, WorkDate.ToDateTime(new(18, 0), DateTimeKind.Utc), "race fixture"));
        Assert.True(request.Succeeded, request.Message);
        f.RequestId = request.Value!.Id;
        return f;
    }

    private sealed class NoopEmployeeSerializationLock : IEmployeeSerializationLock
    {
        public Task AcquireAsync(Guid tenantId, Guid employeeId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class SqliteEmployeeSerializationCoordinator : DbTransactionInterceptor, IEmployeeSerializationLock
    {
        private readonly SemaphoreSlim gate = new(1, 1);

        public Task AcquireAsync(Guid tenantId, Guid employeeId, CancellationToken cancellationToken = default) => gate.WaitAsync(cancellationToken);

        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            gate.Release();
            return base.TransactionCommittedAsync(transaction, eventData, cancellationToken);
        }

        public override Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            gate.Release();
            return base.TransactionRolledBackAsync(transaction, eventData, cancellationToken);
        }
    }

    private static async Task<AttendanceWorkflowTests.WorkflowFixture> MonthlyFixtureAsync() => await AttendanceWorkflowTests.FixtureAsync();

    private static async Task<TResult[]> RaceAsync<TResult>(AttendanceWorkflowTests.WorkflowFixture fixture, Func<HRMS.Infrastructure.Persistence.HrmsDbContext, TestTenantContext, Task<TResult>> first, Func<HRMS.Infrastructure.Persistence.HrmsDbContext, TestTenantContext, Task<TResult>> second)
    {
        using var barrier = new Barrier(2);
        async Task<TResult> Run(Func<HRMS.Infrastructure.Persistence.HrmsDbContext, TestTenantContext, Task<TResult>> operation)
        {
            await using var db = fixture.CreateIsolatedContext(fixture.ManagerUserId, out var tenant);
            return await Task.Run(async () => { barrier.SignalAndWait(TimeSpan.FromSeconds(10)); return await operation(db, tenant); });
        }
        return await Task.WhenAll(Run(first), Run(second));
    }
}
