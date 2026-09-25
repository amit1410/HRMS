using System.Data.Common;
using HRMS.Application.Abstractions;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HRMS.Tests;

public sealed class AttendanceOperationsFailureInjectionTests
{
    private static readonly DateOnly WorkDate = new(2026, 9, 10);

    [Fact]
    public async Task Manual_attendance_persistence_failure_retry_safe()
    {
        using var f = await AttendanceWorkflowTests.FixtureAsync();
        await AttendanceWorkflowTests.SeedIncompleteDayAsync(f, WorkDate);
        var request = ManualRequest(f, WorkDate);
        var fail = new FailOnceSaveChangesInterceptor(context => context.ChangeTracker.Entries<AttendanceRegularizationRequest>().Any(x => x.State == EntityState.Added));
        await using (var db = f.CreateIsolatedContext(f.Identity.UserId, out var tenant, fail))
        {
            await Assert.ThrowsAsync<DbUpdateException>(() => new AttendanceOperationsService(db, tenant).SubmitManualAttendanceAsync(request));
        }

        await using (var verify = f.CreateIsolatedContext(f.Identity.UserId, out _))
        {
            Assert.Empty(await verify.AttendanceRegularizationRequests.ToListAsync());
            Assert.Empty(await verify.AttendanceAdjustments.ToListAsync());
            Assert.Empty(await verify.EmployeeAuditLogs.ToListAsync());
            Assert.Equal(EmployeeAttendanceDayStatus.Incomplete, await verify.EmployeeAttendanceDays.Where(x => x.EmployeeId == f.EmployeeId && x.BusinessDate == WorkDate).Select(x => x.Status).SingleAsync());
        }

        Guid requestId;
        await using (var retryDb = f.CreateIsolatedContext(f.Identity.UserId, out var tenant))
        {
            var retry = await new AttendanceOperationsService(retryDb, tenant).SubmitManualAttendanceAsync(request);
            Assert.True(retry.Succeeded, retry.Message);
            requestId = retry.Value!.Id;
        }
        await using (var checkerDb = f.CreateIsolatedContext(f.ManagerUserId, out var checkerTenant))
        {
            var approved = await f.CreateService(checkerDb, checkerTenant, f.ManagerUserId, f.ManagerId).ApproveRegularizationAsync(requestId);
            Assert.True(approved.Succeeded, approved.Message);
        }
        await using var final = f.CreateIsolatedContext(f.Identity.UserId, out _);
        Assert.Single(await final.AttendanceRegularizationRequests.Where(x => x.Id == requestId).ToListAsync());
        Assert.Single(await final.AttendanceAdjustments.Where(x => x.AttendanceRegularizationRequestId == requestId).ToListAsync());
        Assert.Equal(EmployeeAttendanceDayStatus.Present, await final.EmployeeAttendanceDays.Where(x => x.EmployeeId == f.EmployeeId && x.BusinessDate == WorkDate).Select(x => x.Status).SingleAsync());
    }

    [Fact]
    public async Task Approval_failure_does_not_partially_reprocess_day()
    {
        using var f = await PendingManualAsync();
        var fail = new FailOnceSaveChangesInterceptor(context => context.ChangeTracker.Entries<EmployeeAuditLog>().Any(x => x.Entity.FieldName == "RegularizationApproved"));
        await using (var db = f.CreateIsolatedContext(f.ManagerUserId, out var tenant, fail))
        {
            var service = f.CreateService(db, tenant, f.ManagerUserId, f.ManagerId);
            await Assert.ThrowsAsync<DbUpdateException>(() => service.ApproveRegularizationAsync(f.RequestId));
        }

        await using (var verify = f.CreateIsolatedContext(f.ManagerUserId, out _))
        {
            Assert.Equal(AttendanceRequestStatus.Pending, await verify.AttendanceRegularizationRequests.Where(x => x.Id == f.RequestId).Select(x => x.Status).SingleAsync());
            Assert.Empty(await verify.AttendanceAdjustments.Where(x => x.AttendanceRegularizationRequestId == f.RequestId).ToListAsync());
            Assert.Equal(EmployeeAttendanceDayStatus.Incomplete, await verify.EmployeeAttendanceDays.Where(x => x.EmployeeId == f.EmployeeId && x.BusinessDate == WorkDate).Select(x => x.Status).SingleAsync());
            Assert.Empty(await verify.AttendanceRegularizationEvents.Where(x => x.AttendanceRegularizationRequestId == f.RequestId && x.EventType == AttendanceRequestEventType.Approved).ToListAsync());
            Assert.Empty(await verify.EmployeeAuditLogs.Where(x => x.ImportBatchId == f.RequestId && x.FieldName == "RegularizationApproved").ToListAsync());
        }

        await using (var retryDb = f.CreateIsolatedContext(f.ManagerUserId, out var retryTenant))
        {
            var retry = await f.CreateService(retryDb, retryTenant, f.ManagerUserId, f.ManagerId).ApproveRegularizationAsync(f.RequestId);
            Assert.True(retry.Succeeded, retry.Message);
        }
        await using var final = f.CreateIsolatedContext(f.ManagerUserId, out _);
        Assert.Equal(AttendanceRequestStatus.Approved, await final.AttendanceRegularizationRequests.Where(x => x.Id == f.RequestId).Select(x => x.Status).SingleAsync());
        Assert.Single(await final.AttendanceAdjustments.Where(x => x.AttendanceRegularizationRequestId == f.RequestId).ToListAsync());
        Assert.Single(await final.AttendanceRegularizationEvents.Where(x => x.AttendanceRegularizationRequestId == f.RequestId && x.EventType == AttendanceRequestEventType.Approved).ToListAsync());
        Assert.Single(await final.EmployeeAuditLogs.Where(x => x.ImportBatchId == f.RequestId && x.FieldName == "RegularizationApproved").ToListAsync());
    }

    [Fact]
    public async Task Bulk_operation_failure_reports_consistent_results()
    {
        using var f = await AttendanceWorkflowTests.FixtureAsync();
        var secondDate = WorkDate.AddDays(1);
        await AttendanceWorkflowTests.SeedIncompleteDayAsync(f, WorkDate);
        await AttendanceWorkflowTests.SeedIncompleteDayAsync(f, secondDate);
        var first = await SubmitPendingAsync(f, WorkDate);
        var second = await SubmitPendingAsync(f, secondDate);
        var fail = new FailOnceSaveChangesInterceptor(context => context.ChangeTracker.Entries<AttendanceRegularizationEvent>().Any(x => x.Entity.AttendanceRegularizationRequestId == first && x.Entity.EventType == AttendanceRequestEventType.Approved));
        AttendanceBulkActionResponse response;
        await using (var db = f.CreateIsolatedContext(f.ManagerUserId, out var tenant, fail))
        {
            var service = OperationsService(db, tenant);
            var result = await service.ApplyBulkActionAsync([
                new(first, false, true, 1, "bulk failure test"),
                new(second, false, true, 1, "bulk success test")
            ]);
            Assert.True(result.Succeeded, result.Message);
            response = result.Value!;
        }

        Assert.Equal(2, response.Items.Count);
        Assert.Equal("PersistenceFailure", response.Items.Single(x => x.RequestId == first).FailureCode);
        Assert.True(response.Items.Single(x => x.RequestId == second).Success);
        await using (var verify = f.CreateIsolatedContext(f.ManagerUserId, out _))
        {
            Assert.Equal(AttendanceRequestStatus.Pending, await verify.AttendanceRegularizationRequests.Where(x => x.Id == first).Select(x => x.Status).SingleAsync());
            Assert.Equal(AttendanceRequestStatus.Approved, await verify.AttendanceRegularizationRequests.Where(x => x.Id == second).Select(x => x.Status).SingleAsync());
            Assert.Empty(await verify.AttendanceAdjustments.Where(x => x.AttendanceRegularizationRequestId == first).ToListAsync());
            Assert.Single(await verify.AttendanceAdjustments.Where(x => x.AttendanceRegularizationRequestId == second).ToListAsync());
        }

        await using (var retryDb = f.CreateIsolatedContext(f.ManagerUserId, out var tenant))
        {
            var retry = await OperationsService(retryDb, tenant).ApplyBulkActionAsync([new(first, false, true, 1, "retry failed item")]);
            Assert.True(retry.Succeeded, retry.Message);
            Assert.True(retry.Value!.Items.Single().Success);
        }
        await using var final = f.CreateIsolatedContext(f.ManagerUserId, out _);
        Assert.Single(await final.AttendanceAdjustments.Where(x => x.AttendanceRegularizationRequestId == first).ToListAsync());
        Assert.Single(await final.AttendanceRegularizationEvents.Where(x => x.AttendanceRegularizationRequestId == first && x.EventType == AttendanceRequestEventType.Approved).ToListAsync());
        Assert.Single(await final.EmployeeAuditLogs.Where(x => x.ImportBatchId == first && x.FieldName == "RegularizationApproved").ToListAsync());
    }

    [Fact]
    public async Task Finalization_failure_does_not_false_lock_period()
    {
        await using var f = await Phase6BAcceptanceData.ReadyAsync();
        var fail = new FailOnceSaveChangesInterceptor(context => context.ChangeTracker.Entries<AttendancePeriodEvent>().Any(x => x.Entity.EventType == AttendancePeriodEventType.Closed));
        await using (var db = f.CreateIsolatedContext(Guid.NewGuid(), fail))
        {
            var result = await new AttendanceMonthlyProcessor(db, new TestTenantContext(f.TenantId)).CloseAsync(f.PeriodId);
            Assert.False(result.Succeeded);
        }
        await using (var verify = f.CreateIsolatedContext(Guid.NewGuid()))
        {
            Assert.NotEqual(AttendancePeriodStatus.Closed, await verify.AttendancePeriods.Where(x => x.Id == f.PeriodId).Select(x => x.Status).SingleAsync());
            Assert.Empty(await verify.PayrollAttendanceSnapshots.Where(x => x.AttendancePeriodId == f.PeriodId && x.IsCurrent).ToListAsync());
            Assert.Empty(await verify.AttendancePeriodEvents.Where(x => x.AttendancePeriodId == f.PeriodId && x.EventType == AttendancePeriodEventType.Closed).ToListAsync());
        }
        await using (var retryDb = f.CreateIsolatedContext(Guid.NewGuid()))
        {
            var retry = await new AttendanceMonthlyProcessor(retryDb, new TestTenantContext(f.TenantId)).CloseAsync(f.PeriodId);
            Assert.True(retry.Succeeded, retry.Message);
        }
        await using var final = f.CreateIsolatedContext(Guid.NewGuid());
        Assert.Equal(AttendancePeriodStatus.Closed, await final.AttendancePeriods.Where(x => x.Id == f.PeriodId).Select(x => x.Status).SingleAsync());
        Assert.Single(await final.PayrollAttendanceSnapshots.Where(x => x.AttendancePeriodId == f.PeriodId && x.IsCurrent).ToListAsync());
    }

    [Fact]
    public async Task Reopen_failure_preserves_previous_finalized_state()
    {
        await using var f = await Phase6BAcceptanceData.FinalizedAsync();
        var snapshot = await f.Db.PayrollAttendanceSnapshots.SingleAsync(x => x.AttendancePeriodId == f.PeriodId && x.IsCurrent);
        var fail = new FailOnceSaveChangesInterceptor(context => context.ChangeTracker.Entries<AttendancePeriodEvent>().Any(x => x.Entity.EventType == AttendancePeriodEventType.Reopened));
        await using (var db = f.CreateIsolatedContext(Guid.NewGuid(), fail))
        {
            var result = await new AttendanceMonthlyProcessor(db, new TestTenantContext(f.TenantId)).ReopenAsync(f.PeriodId, new("injected reopen failure"));
            Assert.False(result.Succeeded);
        }
        await using (var verify = f.CreateIsolatedContext(Guid.NewGuid()))
        {
            Assert.Equal(AttendancePeriodStatus.Closed, await verify.AttendancePeriods.Where(x => x.Id == f.PeriodId).Select(x => x.Status).SingleAsync());
            var retained = await verify.PayrollAttendanceSnapshots.SingleAsync(x => x.Id == snapshot.Id);
            Assert.Equal(1, retained.Version);
            Assert.True(retained.IsCurrent);
            Assert.Empty(await verify.AttendancePeriodEvents.Where(x => x.AttendancePeriodId == f.PeriodId && x.EventType == AttendancePeriodEventType.Reopened).ToListAsync());
        }
        await using var retryDb = f.CreateIsolatedContext(Guid.NewGuid());
        Assert.True((await new AttendanceMonthlyProcessor(retryDb, new TestTenantContext(f.TenantId)).ReopenAsync(f.PeriodId, new("retry reopen"))).Succeeded);
    }

    [Fact]
    public async Task Refinalization_failure_preserves_previous_current_version()
    {
        await using var f = await Phase6BAcceptanceData.FinalizedAsync();
        var payroll = await f.CalculatePayrollAsync();
        Assert.True(payroll.Succeeded, payroll.Message);
        var historicalPayroll = await f.Db.PayrollResults.AsNoTracking().SingleAsync(x => x.IsCurrent);
        var v1 = await f.Db.PayrollAttendanceSnapshots.SingleAsync(x => x.AttendancePeriodId == f.PeriodId && x.Version == 1);
        Assert.True((await f.Processor.ReopenAsync(f.PeriodId, new("prepare re-finalization failure"))).Succeeded);
        Assert.True((await f.Processor.ProcessAsync(f.PeriodId)).Succeeded);
        var fail = new FailOnceSaveChangesInterceptor(context => context.ChangeTracker.Entries<PayrollAttendanceSnapshot>().Any(x => x.State == EntityState.Added && x.Entity.Version == 2));
        await using (var db = f.CreateIsolatedContext(Guid.NewGuid(), fail))
        {
            var result = await new AttendanceMonthlyProcessor(db, new TestTenantContext(f.TenantId)).CloseAsync(f.PeriodId);
            Assert.False(result.Succeeded);
        }
        await using (var verify = f.CreateIsolatedContext(Guid.NewGuid()))
        {
            var current = await verify.PayrollAttendanceSnapshots.Where(x => x.AttendancePeriodId == f.PeriodId && x.IsCurrent).ToListAsync();
            Assert.Single(current);
            Assert.Equal(v1.Id, current[0].Id);
            Assert.Equal(1, await verify.PayrollAttendanceSnapshots.Where(x => x.AttendancePeriodId == f.PeriodId && x.Version == 1).Select(x => x.Version).SingleAsync());
            Assert.Empty(await verify.PayrollAttendanceSnapshots.Where(x => x.AttendancePeriodId == f.PeriodId && x.Version == 2).ToListAsync());
            var retainedPayroll = await verify.PayrollResults.AsNoTracking().SingleAsync(x => x.Id == historicalPayroll.Id);
            Assert.Equal(v1.Id, retainedPayroll.AttendanceSnapshotId);
            Assert.Equal(1, retainedPayroll.AttendanceVersion);
        }
        await using (var retryDb = f.CreateIsolatedContext(Guid.NewGuid()))
        {
            var retry = await new AttendanceMonthlyProcessor(retryDb, new TestTenantContext(f.TenantId)).CloseAsync(f.PeriodId);
            Assert.True(retry.Succeeded, retry.Message);
        }
        await using var final = f.CreateIsolatedContext(Guid.NewGuid());
        Assert.Equal(1, await final.PayrollAttendanceSnapshots.CountAsync(x => x.AttendancePeriodId == f.PeriodId && x.IsCurrent));
        Assert.Equal(2, await final.PayrollAttendanceSnapshots.CountAsync(x => x.AttendancePeriodId == f.PeriodId));
        Assert.Equal(v1.Id, await final.PayrollResults.Where(x => x.Id == historicalPayroll.Id).Select(x => x.AttendanceSnapshotId).SingleAsync());
    }

    [Fact]
    public async Task Audit_persistence_failure_does_not_commit_mutation_without_audit()
    {
        using var f = await AttendanceWorkflowTests.FixtureAsync();
        var day = new EmployeeAttendanceDay
        {
            Id = Guid.NewGuid(), TenantId = f.TenantId, EmployeeId = f.EmployeeId, BusinessDate = WorkDate,
            Status = EmployeeAttendanceDayStatus.Present, ExpectedWorkMinutes = 480, WorkedMinutes = 480,
            IsLateIn = true, ScheduledStartUtc = WorkDate.ToDateTime(new(9, 0), DateTimeKind.Utc),
            FirstPunchAtUtc = WorkDate.ToDateTime(new(9, 20), DateTimeKind.Utc), ProcessedAtUtc = DateTime.UtcNow
        };
        f.Context.EmployeeAttendanceDays.Add(day);
        await f.Context.SaveChangesAsync();
        var fail = new FailOnceSaveChangesInterceptor(context => context.ChangeTracker.Entries<EmployeeAuditLog>().Any(x => x.State == EntityState.Added && x.Entity.FieldName?.Contains("LateArrival", StringComparison.Ordinal) == true));
        await using (var db = f.CreateIsolatedContext(f.ManagerUserId, out var tenant, fail))
        {
            var service = new AttendanceOperationsService(db, tenant);
            await Assert.ThrowsAsync<DbUpdateException>(() => service.ResolveExceptionAsync(new(day.Id, AttendanceExceptionType.LateArrival, "Acknowledge", "verified and accepted", 1)));
        }
        await using (var verify = f.CreateIsolatedContext(f.ManagerUserId, out _))
        {
            Assert.Empty(await verify.AttendanceExceptionResolutions.Where(x => x.AttendanceDayId == day.Id).ToListAsync());
            Assert.Empty(await verify.EmployeeAuditLogs.Where(x => x.RecordId == day.Id && x.FieldName != null && x.FieldName.Contains("LateArrival")).ToListAsync());
            var persisted = await verify.EmployeeAttendanceDays.SingleAsync(x => x.Id == day.Id);
            Assert.True(persisted.IsLateIn);
            Assert.Equal(WorkDate.ToDateTime(new(9, 20), DateTimeKind.Utc), persisted.FirstPunchAtUtc);
        }
        await using var retryDb = f.CreateIsolatedContext(f.ManagerUserId, out var retryTenant);
        var retry = await new AttendanceOperationsService(retryDb, retryTenant).ResolveExceptionAsync(new(day.Id, AttendanceExceptionType.LateArrival, "Acknowledge", "verified and accepted", 1));
        Assert.True(retry.Succeeded, retry.Message);
        Assert.Single(await retryDb.AttendanceExceptionResolutions.Where(x => x.AttendanceDayId == day.Id).ToListAsync());
        Assert.Single(await retryDb.EmployeeAuditLogs.Where(x => x.RecordId == day.Id && x.FieldName != null && x.FieldName.Contains("LateArrival")).ToListAsync());
    }

    [Fact]
    public async Task Export_failure_does_not_modify_attendance()
    {
        using var f = await AttendanceWorkflowTests.FixtureAsync();
        var day = new EmployeeAttendanceDay
        {
            Id = Guid.NewGuid(), TenantId = f.TenantId, EmployeeId = f.EmployeeId, BusinessDate = WorkDate,
            Status = EmployeeAttendanceDayStatus.Present, ExpectedWorkMinutes = 480, WorkedMinutes = 480,
            IsLateIn = true, FirstPunchAtUtc = WorkDate.ToDateTime(new(9, 15), DateTimeKind.Utc),
            ProcessedAtUtc = DateTime.UtcNow
        };
        f.Context.EmployeeAttendanceDays.Add(day);
        await f.Context.SaveChangesAsync();
        var fail = new FailOnceReaderInterceptor();
        await using (var db = f.CreateIsolatedContext(f.ManagerUserId, out var tenant, fail))
        {
            var service = new AttendanceOperationsService(db, tenant);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExportExceptionsAsync(new() { FromDate = WorkDate, ToDate = WorkDate, EmployeeId = f.EmployeeId, ExceptionType = AttendanceExceptionType.LateArrival, Page = 1, PageSize = 10 }));
        }
        await using (var retryDb = f.CreateIsolatedContext(f.ManagerUserId, out var retryTenant))
        {
            var retry = await new AttendanceOperationsService(retryDb, retryTenant).ExportExceptionsAsync(new() { FromDate = WorkDate, ToDate = WorkDate, EmployeeId = f.EmployeeId, ExceptionType = AttendanceExceptionType.LateArrival, Page = 1, PageSize = 10 });
            Assert.True(retry.Succeeded, retry.Message);
            Assert.Equal(1, retry.Value!.RowCount);
        }
        await using var verify = f.CreateIsolatedContext(f.ManagerUserId, out _);
        var persisted = await verify.EmployeeAttendanceDays.SingleAsync(x => x.Id == day.Id);
        Assert.Equal(EmployeeAttendanceDayStatus.Present, persisted.Status);
        Assert.True(persisted.IsLateIn);
        Assert.Equal(WorkDate.ToDateTime(new(9, 15), DateTimeKind.Utc), persisted.FirstPunchAtUtc);
        Assert.Empty(await verify.AttendanceAdjustments.ToListAsync());
        Assert.Empty(await verify.AttendanceExceptionResolutions.ToListAsync());
        Assert.Empty(await verify.EmployeeAuditLogs.ToListAsync());
    }

    private static ManualAttendanceRequest ManualRequest(AttendanceWorkflowTests.WorkflowFixture f, DateOnly date) =>
        new(f.EmployeeId, date, AttendanceRegularizationType.CorrectInOutTime,
            date.ToDateTime(new(9, 0), DateTimeKind.Utc), date.ToDateTime(new(18, 0), DateTimeKind.Utc),
            "verified manual attendance", "failure injection", 1);

    private static async Task<AttendanceWorkflowTests.WorkflowFixture> PendingManualAsync()
    {
        var f = await AttendanceWorkflowTests.FixtureAsync();
        await AttendanceWorkflowTests.SeedIncompleteDayAsync(f, WorkDate);
        var result = await new AttendanceOperationsService(f.Context, new TestTenantContext(f.TenantId, f.Identity.UserId)).SubmitManualAttendanceAsync(ManualRequest(f, WorkDate));
        Assert.True(result.Succeeded, result.Message);
        f.RequestId = result.Value!.Id;
        return f;
    }

    private static async Task<Guid> SubmitPendingAsync(AttendanceWorkflowTests.WorkflowFixture f, DateOnly date)
    {
        var result = await new AttendanceOperationsService(f.Context, new TestTenantContext(f.TenantId, f.Identity.UserId)).SubmitManualAttendanceAsync(ManualRequest(f, date));
        Assert.True(result.Succeeded, result.Message);
        return result.Value!.Id;
    }

    private static AttendanceOperationsService OperationsService(HRMS.Infrastructure.Persistence.HrmsDbContext db, TestTenantContext tenant)
    {
        var employment = new EffectiveEmploymentResolver(db, tenant);
        var calendar = new WorkingDayCalendarResolver(db, tenant, employment);
        var foundation = new AttendanceFoundationService(db, tenant, employment, calendar);
        var processor = new AttendanceDayProcessor(db, tenant, foundation, TimeProvider.System, new AttendancePeriodLockService(db, tenant));
        return new AttendanceOperationsService(db, tenant, processor: processor);
    }

    private sealed class FailOnceSaveChangesInterceptor(Func<DbContext, bool> shouldFail) : SaveChangesInterceptor
    {
        private int failed;
        private void MaybeFail(DbContext? context)
        {
            if (context is not null && Volatile.Read(ref failed) == 0 && shouldFail(context) && Interlocked.Exchange(ref failed, 1) == 0)
                throw new DbUpdateException("Injected Attendance persistence failure.");
        }

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            MaybeFail(eventData.Context);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            MaybeFail(eventData.Context);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class FailOnceReaderInterceptor : DbCommandInterceptor
    {
        private int failed;
        private void MaybeFail()
        {
            if (Interlocked.Exchange(ref failed, 1) == 0) throw new InvalidOperationException("Injected export stream/query failure.");
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            MaybeFail();
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            MaybeFail();
            return ValueTask.FromResult(result);
        }
    }
}
