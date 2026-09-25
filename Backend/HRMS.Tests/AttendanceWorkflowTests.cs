using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.Services;
using HRMS.API.Controllers;
using HRMS.API.Security;
using HRMS.Domain.Entities;
using HRMS.Domain.Authorization;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class AttendanceWorkflowTests
{
    [Fact]
    public async Task Regularization_approval_rolls_back_when_reprocessing_fails()
    {
        using var f = await FixtureAsync();
        var date = new DateOnly(2026, 9, 10);
        await SeedIncompleteDayAsync(f, date);
        var request = (await Service(f).SubmitRegularizationAsync(new(date, AttendanceRegularizationType.MissingOutPunch, null, date.ToDateTime(new(18, 0), DateTimeKind.Utc), "forgot"))).Value!;
        f.Identity.EmployeeId = f.ManagerId;

        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(f, new FailingProcessor()).ApproveRegularizationAsync(request.Id));

        await using var verify = f.CreateContext(f.TenantId, out _);
        Assert.Equal(AttendanceRequestStatus.Pending, (await verify.AttendanceRegularizationRequests.SingleAsync(x => x.Id == request.Id)).Status);
        Assert.Empty(await verify.AttendanceRegularizationEvents.Where(x => x.AttendanceRegularizationRequestId == request.Id && x.EventType == AttendanceRequestEventType.Approved).ToListAsync());
        Assert.Empty(await verify.AttendanceAdjustments.Where(x => x.AttendanceRegularizationRequestId == request.Id).ToListAsync());
        Assert.Equal(EmployeeAttendanceDayStatus.Incomplete, (await verify.EmployeeAttendanceDays.SingleAsync()).Status);

        f.Context.ClearChangeTracker();
        var retry = await Service(f).ApproveRegularizationAsync(request.Id);
        Assert.True(retry.Succeeded, retry.Message);
        Assert.Single(await f.Context.AttendanceAdjustments.Where(x => x.AttendanceRegularizationRequestId == request.Id).ToListAsync());
        Assert.Equal(2, await f.Context.AttendanceRegularizationEvents.CountAsync(x => x.AttendanceRegularizationRequestId == request.Id));
    }

    [Fact]
    public async Task On_duty_approval_rolls_back_all_dates_when_reprocessing_fails()
    {
        using var f = await FixtureAsync();
        var start = new DateOnly(2026, 9, 10);
        var request = (await Service(f).SubmitOnDutyAsync(new(start, start.AddDays(1), "Client visit"))).Value!;
        f.Identity.EmployeeId = f.ManagerId;

        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(f, new FailingProcessor()).ApproveOnDutyAsync(request.Id));

        await using var verify = f.CreateContext(f.TenantId, out _);
        Assert.Equal(AttendanceRequestStatus.Pending, (await verify.AttendanceOnDutyRequests.SingleAsync(x => x.Id == request.Id)).Status);
        Assert.Empty(await verify.AttendanceOnDutyEvents.Where(x => x.AttendanceOnDutyRequestId == request.Id && x.EventType == AttendanceRequestEventType.Approved).ToListAsync());
        Assert.Empty(await verify.EmployeeAttendanceDays.ToListAsync());

        f.Context.ClearChangeTracker();
        var retry = await Service(f).ApproveOnDutyAsync(request.Id);
        Assert.True(retry.Succeeded, retry.Message);
        Assert.Equal(2, await f.Context.AttendanceOnDutyEvents.CountAsync(x => x.AttendanceOnDutyRequestId == request.Id));
        Assert.Equal(2, await f.Context.EmployeeAttendanceDays.CountAsync(x => x.Status == EmployeeAttendanceDayStatus.OnDuty));
    }

    [Fact]
    public async Task OnDuty_approval_removes_resolved_absence_from_active_exceptions()
    {
        using var f = await FixtureAsync();
        var date = new DateOnly(2026, 10, 9);
        var initial = await f.Processor.ProcessAsync(f.EmployeeId, date);
        Assert.True(initial.Succeeded, initial.Message);
        Assert.Equal(EmployeeAttendanceDayStatus.Absent, initial.Value!.Status);
        var query = new AttendanceOperationsService(f.Context, f.Inner.TenantContext);
        var before = await query.GetOperationalExceptionsAsync(new() { FromDate = date, ToDate = date, ExceptionType = AttendanceExceptionType.Absent, Page = 1, PageSize = 10 });
        Assert.Single(before.Value!.Items);

        var request = (await Service(f).SubmitOnDutyAsync(new(date, date, "Client visit"))).Value!;
        f.Identity.EmployeeId = f.ManagerId;
        var approval = await Service(f).ApproveOnDutyAsync(request.Id);

        Assert.True(approval.Succeeded, approval.Message);
        Assert.Equal(EmployeeAttendanceDayStatus.OnDuty, (await f.Context.EmployeeAttendanceDays.SingleAsync(x => x.EmployeeId == f.EmployeeId && x.BusinessDate == date)).Status);
        var after = await query.GetOperationalExceptionsAsync(new() { FromDate = date, ToDate = date, ExceptionType = AttendanceExceptionType.Absent, Page = 1, PageSize = 10 });
        Assert.Empty(after.Value!.Items);
        Assert.Empty(await f.Context.AttendanceExceptionResolutions.ToListAsync());
    }

    [Fact]
    public async Task Concurrent_close_and_regularization_approval_never_apply_after_close()
    {
        using var f = await FixtureAsync();
        f.Context.Users.Add(new User { Id = f.Inner.TenantContext.UserId!.Value, TenantId = f.TenantId, Email = "period-close@example.test", FirstName = "Period", LastName = "Closer" });
        await f.Context.SaveChangesAsync();
        var date = new DateOnly(2026, 9, 10);
        await SeedIncompleteDayAsync(f, date);
        var request = (await Service(f).SubmitRegularizationAsync(new(date, AttendanceRegularizationType.MissingOutPunch, null, date.ToDateTime(new(18, 0), DateTimeKind.Utc), "race"))).Value!;
        var monthly = new AttendanceMonthlyProcessor(f.Context, f.Inner.TenantContext);
        var period = (await monthly.CreatePeriodAsync(new(2026, 9))).Value!;
        Assert.True((await monthly.ProcessAsync(period.Id)).Succeeded);

        await using var closeDb = f.CreateContext(f.TenantId, out var closeTenant);
        await using var approvalDb = f.CreateContext(f.TenantId, out var approvalTenant);
        var closeTask = new AttendanceMonthlyProcessor(closeDb, closeTenant).CloseAsync(period.Id);
        var approvalIdentity = new MutableIdentity(f.TenantId, f.ManagerUserId, f.ManagerId, f.ManagerUserId, f.ManagerId);
        var approvalCalendar = new WorkingDayCalendarResolver(approvalDb, approvalTenant, new EffectiveEmploymentResolver(approvalDb, approvalTenant));
        var approvalProcessor = new AttendanceDayProcessor(approvalDb, approvalTenant, new AttendanceFoundationService(approvalDb, approvalTenant, new EffectiveEmploymentResolver(approvalDb, approvalTenant), approvalCalendar), new FixedClock(new DateTimeOffset(2026, 9, 10, 20, 0, 0, TimeSpan.Zero)), new AttendancePeriodLockService(approvalDb, approvalTenant));
        var approvalService = new AttendanceWorkflowService(approvalDb, approvalIdentity, f.Managers, approvalProcessor, new FixedClock(new DateTimeOffset(2026, 9, 10, 20, 0, 0, TimeSpan.Zero)), new AttendancePeriodLockService(approvalDb, approvalTenant));
        var approvalTask = approvalService.ApproveRegularizationAsync(request.Id);
        await Task.WhenAll(closeTask, approvalTask);
        var closeResult = await closeTask;
        var approvalResult = await approvalTask;

        await using var verify = f.CreateContext(f.TenantId, out _);
        var persistedPeriod = await verify.AttendancePeriods.AsNoTracking().SingleAsync(x => x.Id == period.Id);
        var persistedRequest = await verify.AttendanceRegularizationRequests.AsNoTracking().SingleAsync(x => x.Id == request.Id);
        Assert.False(persistedPeriod.Status == AttendancePeriodStatus.Closed && persistedRequest.Status == AttendanceRequestStatus.Approved);
        Assert.NotEqual(AttendancePeriodStatus.Closed, persistedPeriod.Status);
        Assert.True(closeResult.Status == ResultStatus.Conflict || closeResult.Status == ResultStatus.ServiceUnavailable);
        Assert.True(approvalResult.Succeeded);
    }

    [Fact]
    public async Task Concurrent_close_and_on_duty_approval_never_apply_after_close()
    {
        using var f = await FixtureAsync();
        f.Context.Users.Add(new User { Id = f.Inner.TenantContext.UserId!.Value, TenantId = f.TenantId, Email = "period-close-od@example.test", FirstName = "Period", LastName = "Closer" });
        await f.Context.SaveChangesAsync();
        var submitted = (await Service(f).SubmitOnDutyAsync(new(new(2026, 9, 10), new(2026, 9, 10), "race"))).Value!;
        var monthly = new AttendanceMonthlyProcessor(f.Context, f.Inner.TenantContext);
        var period = (await monthly.CreatePeriodAsync(new(2026, 9))).Value!;
        Assert.True((await monthly.ProcessAsync(period.Id)).Succeeded);

        await using var closeDb = f.CreateContext(f.TenantId, out var closeTenant);
        await using var approvalDb = f.CreateContext(f.TenantId, out var approvalTenant);
        var closeTask = new AttendanceMonthlyProcessor(closeDb, closeTenant).CloseAsync(period.Id);
        var approvalIdentity = new MutableIdentity(f.TenantId, f.ManagerUserId, f.ManagerId, f.ManagerUserId, f.ManagerId);
        var approvalCalendar = new WorkingDayCalendarResolver(approvalDb, approvalTenant, new EffectiveEmploymentResolver(approvalDb, approvalTenant));
        var approvalProcessor = new AttendanceDayProcessor(approvalDb, approvalTenant, new AttendanceFoundationService(approvalDb, approvalTenant, new EffectiveEmploymentResolver(approvalDb, approvalTenant), approvalCalendar), new FixedClock(new DateTimeOffset(2026, 9, 10, 20, 0, 0, TimeSpan.Zero)), new AttendancePeriodLockService(approvalDb, approvalTenant));
        var approvalService = new AttendanceWorkflowService(approvalDb, approvalIdentity, f.Managers, approvalProcessor, new FixedClock(new DateTimeOffset(2026, 9, 10, 20, 0, 0, TimeSpan.Zero)), new AttendancePeriodLockService(approvalDb, approvalTenant));
        var approvalTask = approvalService.ApproveOnDutyAsync(submitted.Id);
        await Task.WhenAll(closeTask, approvalTask);
        var closeResult = await closeTask;
        var approvalResult = await approvalTask;

        await using var verify = f.CreateContext(f.TenantId, out _);
        var persistedPeriod = await verify.AttendancePeriods.AsNoTracking().SingleAsync(x => x.Id == period.Id);
        var persistedRequest = await verify.AttendanceOnDutyRequests.AsNoTracking().SingleAsync(x => x.Id == submitted.Id);
        Assert.False(persistedPeriod.Status == AttendancePeriodStatus.Closed && persistedRequest.Status == AttendanceRequestStatus.Approved);
        Assert.NotEqual(AttendancePeriodStatus.Closed, persistedPeriod.Status);
        Assert.True(closeResult.Status == ResultStatus.Conflict || closeResult.Status == ResultStatus.ServiceUnavailable);
        Assert.True(approvalResult.Succeeded);
    }

    [Fact]
    public async Task Workflow_statuses_are_configured_as_optimistic_concurrency_tokens()
    {
        using var f = await FixtureAsync();
        var regularization = f.Context.Model.FindEntityType(typeof(AttendanceRegularizationRequest))!.FindProperty(nameof(AttendanceRegularizationRequest.ConcurrencyVersion));
        var onDuty = f.Context.Model.FindEntityType(typeof(AttendanceOnDutyRequest))!.FindProperty(nameof(AttendanceOnDutyRequest.ConcurrencyVersion));

        Assert.True(regularization!.IsConcurrencyToken);
        Assert.True(onDuty!.IsConcurrencyToken);
    }

    [Fact]
    public async Task Stale_regularization_transition_is_rejected_by_the_database()
    {
        using var f = await FixtureAsync();
        var date = new DateOnly(2026, 9, 10);
        await SeedIncompleteDayAsync(f, date);
        var request = (await Service(f).SubmitRegularizationAsync(new(date, AttendanceRegularizationType.MissingOutPunch, null, date.ToDateTime(new(18, 0), DateTimeKind.Utc), "forgot"))).Value!;

        await using var first = f.CreateContext(f.TenantId, out _);
        await using var second = f.CreateContext(f.TenantId, out _);
        var firstRequest = await first.AttendanceRegularizationRequests.SingleAsync(x => x.Id == request.Id);
        var secondRequest = await second.AttendanceRegularizationRequests.SingleAsync(x => x.Id == request.Id);
        firstRequest.Status = AttendanceRequestStatus.Approved;
        firstRequest.ConcurrencyVersion++;
        secondRequest.Status = AttendanceRequestStatus.Rejected;
        secondRequest.ConcurrencyVersion++;

        await first.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task Linked_employee_submits_regularization_and_history_is_recorded()
    {
        using var f = await FixtureAsync();
        var date = new DateOnly(2026, 10, 10);
        await SeedIncompleteDayAsync(f, date);

        var result = await Service(f).SubmitRegularizationAsync(new(date, AttendanceRegularizationType.MissingOutPunch, date.ToDateTime(new(9, 0), DateTimeKind.Utc), date.ToDateTime(new(18, 0), DateTimeKind.Utc), "Forgot to mark out"));

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(AttendanceRequestStatus.Pending, result.Value!.Status);
        Assert.Single(result.Value.Events);
        Assert.Equal(AttendanceRequestEventType.Submitted, result.Value.Events[0].EventType);
        Assert.Equal(f.EmployeeId, result.Value.EmployeeId);
    }

    [Fact]
    public async Task Regularization_rejects_future_clean_present_and_duplicate_pending_requests()
    {
        using var f = await FixtureAsync();
        var future = new DateOnly(2099, 1, 1);
        var futureResult = await Service(f).SubmitRegularizationAsync(new(future, AttendanceRegularizationType.CorrectInOutTime, null, null, "future"));
        Assert.Equal(ResultStatus.ValidationFailed, futureResult.Status);

        var presentDate = new DateOnly(2026, 9, 10);
        f.Context.EmployeeAttendanceDays.Add(new EmployeeAttendanceDay { Id = Guid.NewGuid(), TenantId = f.TenantId, EmployeeId = f.EmployeeId, BusinessDate = presentDate, Status = EmployeeAttendanceDayStatus.Present });
        await f.Context.SaveChangesAsync();
        var clean = await Service(f).SubmitRegularizationAsync(new(presentDate, AttendanceRegularizationType.CorrectInTime, presentDate.ToDateTime(new(9, 0), DateTimeKind.Utc), null, "clean"));
        Assert.Equal(ResultStatus.Conflict, clean.Status);

        var date = new DateOnly(2026, 9, 11);
        await SeedIncompleteDayAsync(f, date);
        var input = new RegularizationRequestInput(date, AttendanceRegularizationType.MissingOutPunch, null, date.ToDateTime(new(18, 0), DateTimeKind.Utc), "first");
        Assert.True((await Service(f).SubmitRegularizationAsync(input)).Succeeded);
        Assert.Equal(ResultStatus.Conflict, (await Service(f).SubmitRegularizationAsync(input)).Status);
    }

    [Fact]
    public async Task Employee_lists_owns_request_and_can_cancel_pending_but_not_approved()
    {
        using var f = await FixtureAsync();
        var date = new DateOnly(2026, 9, 10);
        await SeedIncompleteDayAsync(f, date);
        var submitted = (await Service(f).SubmitRegularizationAsync(new(date, AttendanceRegularizationType.MissingOutPunch, null, date.ToDateTime(new(18, 0), DateTimeKind.Utc), "missing"))).Value!;

        Assert.Single((await Service(f).GetMyRegularizationsAsync(new TestPageQuery())).Value!.Items);
        var cancelled = await Service(f).CancelRegularizationAsync(submitted.Id);
        Assert.Equal(AttendanceRequestStatus.Cancelled, cancelled.Value!.Status);
        Assert.Equal(ResultStatus.Conflict, (await Service(f).CancelRegularizationAsync(submitted.Id)).Status);
    }

    [Fact]
    public async Task Manager_approval_creates_adjustment_reprocesses_day_and_preserves_raw_punches()
    {
        using var f = await FixtureAsync();
        var date = new DateOnly(2026, 9, 10);
        await SeedIncompleteDayAsync(f, date);
        var punchId = Guid.NewGuid();
        f.Context.AttendancePunches.Add(new AttendancePunch { Id = punchId, TenantId = f.TenantId, EmployeeId = f.EmployeeId, BusinessDate = date, PunchAtUtc = date.ToDateTime(new(9, 0), DateTimeKind.Utc), Direction = PunchDirection.In, Source = PunchSource.Biometric, ExternalPunchId = "raw-1", CapturedAtUtc = DateTime.UtcNow });
        await f.Context.SaveChangesAsync();
        var request = (await Service(f).SubmitRegularizationAsync(new(date, AttendanceRegularizationType.MissingOutPunch, null, date.ToDateTime(new(18, 0), DateTimeKind.Utc), "forgot"))).Value!;

        f.Identity.EmployeeId = f.ManagerId;
        var approved = await Service(f).ApproveRegularizationAsync(request.Id);

        Assert.True(approved.Succeeded, approved.Message);
        Assert.Equal(AttendanceRequestStatus.Approved, approved.Value!.Status);
        Assert.Single(await f.Context.AttendanceAdjustments.Where(x => x.AttendanceRegularizationRequestId == request.Id).ToListAsync());
        Assert.Equal(2, await f.Context.AttendanceRegularizationEvents.CountAsync(x => x.AttendanceRegularizationRequestId == request.Id));
        Assert.Single(await f.Context.AttendancePunches.Where(x => x.Id == punchId).ToListAsync());
        var day = await f.Context.EmployeeAttendanceDays.SingleAsync(x => x.EmployeeId == f.EmployeeId && x.BusinessDate == date);
        Assert.Equal(EmployeeAttendanceDayStatus.Present, day.Status);
        Assert.Equal(date.ToDateTime(new(18, 0), DateTimeKind.Utc), day.LastPunchAtUtc);
        Assert.Equal(1, day.PunchCount);
        Assert.Equal(ResultStatus.Conflict, (await Service(f).ApproveRegularizationAsync(request.Id)).Status);
    }

    [Fact]
    public async Task Manager_rejection_does_not_change_attendance_or_punches()
    {
        using var f = await FixtureAsync();
        var date = new DateOnly(2026, 9, 10);
        await SeedIncompleteDayAsync(f, date);
        var request = (await Service(f).SubmitRegularizationAsync(new(date, AttendanceRegularizationType.MissingOutPunch, null, date.ToDateTime(new(18, 0), DateTimeKind.Utc), "forgot"))).Value!;
        f.Identity.EmployeeId = f.ManagerId;

        var rejected = await Service(f).RejectRegularizationAsync(request.Id, "Insufficient explanation");

        Assert.True(rejected.Succeeded);
        Assert.Equal(AttendanceRequestStatus.Rejected, rejected.Value!.Status);
        Assert.Empty(await f.Context.AttendanceAdjustments.ToListAsync());
        Assert.Equal(EmployeeAttendanceDayStatus.Incomplete, (await f.Context.EmployeeAttendanceDays.SingleAsync()).Status);
        Assert.Equal(2, await f.Context.AttendanceRegularizationEvents.CountAsync());
    }

    [Fact]
    public async Task Effective_manager_controls_queue_and_cross_tenant_request_is_hidden()
    {
        using var f = await FixtureAsync();
        var date = new DateOnly(2026, 9, 10);
        await SeedIncompleteDayAsync(f, date);
        var request = (await Service(f).SubmitRegularizationAsync(new(date, AttendanceRegularizationType.MissingOutPunch, null, date.ToDateTime(new(18, 0), DateTimeKind.Utc), "forgot"))).Value!;
        f.Identity.EmployeeId = f.ManagerId;
        Assert.Single((await Service(f).GetManagerRegularizationsAsync(new AttendanceRegularizationQuery())).Value!.Items);

        var otherTenant = Guid.NewGuid();
        var otherEmployee = Guid.NewGuid();
        await using (var other = f.CreateContext(otherTenant, out _))
        {
            other.Tenants.Add(new Tenant { Id = otherTenant, TenantCode = $"T-{otherTenant:N}"[..20], Host = $"{otherTenant:N}.test", ShardKey = otherTenant.ToString("N"), TenantName = "Other" });
            other.Employees.Add(new Employee { Id = otherEmployee, TenantId = otherTenant, EmployeeCode = "OTHER", FirstName = "Other", LastName = "Employee", DateOfJoining = new(2026, 1, 1) });
            other.Users.Add(new User { Id = Guid.NewGuid(), TenantId = otherTenant, Email = $"{otherEmployee:N}@example.test", FirstName = "Other", LastName = "User", PasswordHash = "test" });
            await other.SaveChangesAsync();
            var otherUserId = await other.Users.Where(x => x.TenantId == otherTenant).Select(x => x.Id).SingleAsync();
            other.AttendanceRegularizationRequests.Add(new AttendanceRegularizationRequest { Id = Guid.NewGuid(), TenantId = otherTenant, EmployeeId = otherEmployee, BusinessDate = date, RequestType = AttendanceRegularizationType.MissingOutPunch, Reason = "other", ProposedOutAtUtc = date.ToDateTime(new(18, 0), DateTimeKind.Utc), SubmittedByUserId = otherUserId, SubmittedAtUtc = DateTime.UtcNow });
            await other.SaveChangesAsync();
        }
        Assert.Null(await f.Context.AttendanceRegularizationRequests.SingleOrDefaultAsync(x => x.TenantId == otherTenant));
        Assert.Single((await Service(f).GetManagerRegularizationsAsync(new AttendanceRegularizationQuery())).Value!.Items);
    }

    [Fact]
    public async Task On_duty_request_validates_overlap_and_approval_projects_on_duty()
    {
        using var f = await FixtureAsync();
        var date = new DateOnly(2026, 9, 10);
        var input = new OnDutyRequestInput(date, date, "Client visit", "Customer meeting", "Noida");
        var submitted = await Service(f).SubmitOnDutyAsync(input);
        Assert.True(submitted.Succeeded, submitted.Message);
        Assert.Equal(ResultStatus.Conflict, (await Service(f).SubmitOnDutyAsync(input)).Status);
        f.Identity.EmployeeId = f.ManagerId;

        var approved = await Service(f).ApproveOnDutyAsync(submitted.Value!.Id);

        Assert.True(approved.Succeeded, approved.Message);
        Assert.Equal(AttendanceRequestStatus.Approved, approved.Value!.Status);
        var day = await f.Context.EmployeeAttendanceDays.SingleAsync(x => x.EmployeeId == f.EmployeeId && x.BusinessDate == date);
        Assert.Equal(EmployeeAttendanceDayStatus.OnDuty, day.Status);
        Assert.Equal(2, await f.Context.AttendanceOnDutyEvents.CountAsync(x => x.AttendanceOnDutyRequestId == submitted.Value.Id));
        Assert.Equal(ResultStatus.Conflict, (await Service(f).ApproveOnDutyAsync(submitted.Value.Id)).Status);
    }

    [Fact]
    public async Task On_duty_cancel_and_reject_are_recorded_without_adjustments()
    {
        using var f = await FixtureAsync();
        var date = new DateOnly(2026, 9, 10);
        var cancel = (await Service(f).SubmitOnDutyAsync(new(date, date, "cancel"))).Value!;
        Assert.Equal(AttendanceRequestStatus.Cancelled, (await Service(f).CancelOnDutyAsync(cancel.Id)).Value!.Status);
        var reject = (await Service(f).SubmitOnDutyAsync(new(date.AddDays(1), date.AddDays(1), "reject"))).Value!;
        f.Identity.EmployeeId = f.ManagerId;
        Assert.Equal(AttendanceRequestStatus.Rejected, (await Service(f).RejectOnDutyAsync(reject.Id, "Not approved")).Value!.Status);
        Assert.Empty(await f.Context.AttendanceOnDutyRequests.Where(x => x.Status == AttendanceRequestStatus.Approved).ToListAsync());
    }

    [Fact]
    public async Task On_duty_approval_rejects_approved_leave_overlap_without_mutating_leave()
    {
        using var f = await FixtureAsync();
        var date = new DateOnly(2026, 9, 10);
        var leaveId = await SeedApprovedLeaveAsync(f, date);
        var request = (await Service(f).SubmitOnDutyAsync(new(date, date, "Client visit"))).Value!;
        f.Identity.EmployeeId = f.ManagerId;

        var result = await Service(f).ApproveOnDutyAsync(request.Id);

        Assert.Equal(ResultStatus.Conflict, result.Status);
        Assert.Equal(AttendanceRequestStatus.Pending, (await f.Context.AttendanceOnDutyRequests.SingleAsync(x => x.Id == request.Id)).Status);
        Assert.Equal(LeaveRequestStatus.Approved, (await f.Context.LeaveRequests.SingleAsync(x => x.Id == leaveId)).Status);
        Assert.Empty(await f.Context.EmployeeAttendanceDays.ToListAsync());
    }

    [Fact]
    public async Task Employee_cannot_read_another_employee_regularization()
    {
        using var f = await FixtureAsync();
        var date = new DateOnly(2026, 9, 10);
        await SeedIncompleteDayAsync(f, date);
        var request = (await Service(f).SubmitRegularizationAsync(new(date, AttendanceRegularizationType.MissingOutPunch, null, date.ToDateTime(new(18, 0), DateTimeKind.Utc), "missing"))).Value!;
        f.Identity.EmployeeId = Guid.NewGuid();

        var result = await Service(f).GetMyRegularizationAsync(request.Id);

        Assert.Equal(ResultStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task Employee_cannot_read_another_employee_on_duty()
    {
        using var f = await FixtureAsync();
        var request = (await Service(f).SubmitOnDutyAsync(new(new(2026, 9, 10), new(2026, 9, 10), "client visit"))).Value!;
        f.Identity.EmployeeId = Guid.NewGuid();

        Assert.Equal(ResultStatus.NotFound, (await Service(f).GetMyOnDutyByIdAsync(request.Id)).Status);
    }

    [Fact]
    public async Task Unlinked_identity_cannot_submit_or_list_workflow_requests()
    {
        using var f = await FixtureAsync();
        var unlinked = new UnlinkedIdentity();
        var service = new AttendanceWorkflowService(f.Context, unlinked, f.Managers, f.Processor);

        Assert.Equal(ResultStatus.NotFound, (await service.SubmitRegularizationAsync(new(new(2026, 9, 10), AttendanceRegularizationType.MissingOutPunch, null, DateTime.UtcNow, "missing"))).Status);
        Assert.Equal(ResultStatus.NotFound, (await service.SubmitOnDutyAsync(new(new(2026, 9, 10), new(2026, 9, 10), "client visit"))).Status);
        Assert.Equal(ResultStatus.NotFound, (await service.GetMyRegularizationsAsync(new TestPageQuery())).Status);
        Assert.Equal(ResultStatus.NotFound, (await service.GetMyOnDutyAsync(new TestPageQuery())).Status);
    }

    [Fact]
    public async Task Employee_cannot_self_approve_regularization_or_on_duty()
    {
        using var f = await FixtureAsync();
        var regularization = (await Service(f).SubmitRegularizationAsync(new(new(2026, 9, 10), AttendanceRegularizationType.MissingOutPunch, null, DateTime.UtcNow, "missing"))).Value!;
        var onDuty = (await Service(f).SubmitOnDutyAsync(new(new(2026, 9, 11), new(2026, 9, 11), "client visit"))).Value!;
        var selfManaging = new AttendanceWorkflowService(f.Context, f.Identity, new SelfManagerResolver(), f.Processor);

        Assert.Equal(ResultStatus.Forbidden, (await selfManaging.ApproveRegularizationAsync(regularization.Id)).Status);
        Assert.Equal(ResultStatus.Forbidden, (await selfManaging.ApproveOnDutyAsync(onDuty.Id)).Status);
        Assert.Equal(AttendanceRequestStatus.Pending, (await f.Context.AttendanceRegularizationRequests.SingleAsync(x => x.Id == regularization.Id)).Status);
        Assert.Equal(AttendanceRequestStatus.Pending, (await f.Context.AttendanceOnDutyRequests.SingleAsync(x => x.Id == onDuty.Id)).Status);
    }

    [Fact]
    public void Workflow_endpoints_require_their_declared_permissions()
    {
        AssertPermission(nameof(AttendanceWorkflowController.MyOnDuty), Permissions.Attendance.View);
        AssertPermission(nameof(AttendanceWorkflowController.MyOnDutyById), Permissions.Attendance.View);
        AssertPermission(nameof(AttendanceWorkflowController.ManagerRegularizations), Permissions.Attendance.RegularizationApprove);
        AssertPermission(nameof(AttendanceWorkflowController.ManagerOnDuty), Permissions.Attendance.OnDutyApprove);
        AssertPermission(nameof(AttendanceWorkflowController.SubmitRegularization), Permissions.Attendance.RegularizationRequest);
        AssertPermission(nameof(AttendanceWorkflowController.CancelRegularization), Permissions.Attendance.RegularizationRequest);
        AssertPermission(nameof(AttendanceWorkflowController.SubmitOnDuty), Permissions.Attendance.OnDutyRequest);
        AssertPermission(nameof(AttendanceWorkflowController.CancelOnDuty), Permissions.Attendance.OnDutyRequest);
    }

    [Fact]
    public async Task Effective_manager_and_boundary_date_control_both_workflow_types()
    {
        using var f = await FixtureAsync();
        var managerB = Guid.NewGuid();
        await f.Inner.AddEmployeeAsync(managerB, "MGRB");
        var boundary = new DateOnly(2026, 9, 11);
        var originalHistory = f.Context.EmployeeEmploymentHistory.Single(x => x.EmployeeId == f.EmployeeId);
        originalHistory.ManagerId = f.ManagerId;
        originalHistory.EffectiveTo = boundary.AddDays(-1);
        f.Context.EmployeeEmploymentHistory.Add(new EmployeeEmploymentHistory { Id = Guid.NewGuid(), TenantId = f.TenantId, EmployeeId = f.EmployeeId, EffectiveFrom = boundary, ManagerId = managerB, EmploymentStatus = EmployeeStatus.Active });
        await f.Context.SaveChangesAsync();
        var historical = new DateOnly(2026, 9, 10);
        var later = boundary;
        await SeedIncompleteDayAsync(f, historical);
        await SeedIncompleteDayAsync(f, later);
        var historicalReg = (await Service(f).SubmitRegularizationAsync(new(historical, AttendanceRegularizationType.MissingOutPunch, null, historical.ToDateTime(new(18, 0), DateTimeKind.Utc), "historical"))).Value!;
        var laterReg = (await Service(f).SubmitRegularizationAsync(new(later, AttendanceRegularizationType.MissingOutPunch, null, later.ToDateTime(new(18, 0), DateTimeKind.Utc), "later"))).Value!;
        var historicalOd = (await Service(f).SubmitOnDutyAsync(new(historical, historical, "historical od"))).Value!;
        var laterOd = (await Service(f).SubmitOnDutyAsync(new(later, later, "later od"))).Value!;
        var managerResolver = new EmployeeManagerResolver(f.Context, f.Inner.TenantContext);
        f.Identity.EmployeeId = f.ManagerId;
        var managerA = new AttendanceWorkflowService(f.Context, f.Identity, managerResolver, f.Processor);
        Assert.True((await managerA.ApproveRegularizationAsync(historicalReg.Id)).Succeeded);
        Assert.True((await managerA.ApproveOnDutyAsync(historicalOd.Id)).Succeeded);
        Assert.Equal(ResultStatus.Forbidden, (await managerA.ApproveRegularizationAsync(laterReg.Id)).Status);
        Assert.Equal(ResultStatus.Forbidden, (await managerA.ApproveOnDutyAsync(laterOd.Id)).Status);
        f.Identity.EmployeeId = managerB;
        var managerBUserId = Guid.NewGuid();
        f.Context.Users.Add(new User { Id = managerBUserId, TenantId = f.TenantId, Email = $"{managerBUserId:N}@example.test", FirstName = "Workflow", LastName = "Manager B", PasswordHash = "test" });
        await f.Context.SaveChangesAsync();
        f.Identity.UserId = managerBUserId;
        var managerBService = new AttendanceWorkflowService(f.Context, f.Identity, managerResolver, f.Processor);
        var laterRegApproval = await managerBService.ApproveRegularizationAsync(laterReg.Id);
        var laterOdApproval = await managerBService.ApproveOnDutyAsync(laterOd.Id);
        Assert.True(laterRegApproval.Succeeded, $"Regularization: {laterRegApproval.Status} {laterRegApproval.Message}");
        Assert.True(laterOdApproval.Succeeded, $"On Duty: {laterOdApproval.Status} {laterOdApproval.Message}");
    }

    [Fact]
    public async Task Multi_day_on_duty_requires_manager_authority_for_every_date()
    {
        using var f = await FixtureAsync();
        var managerB = Guid.NewGuid();
        await f.Inner.AddEmployeeAsync(managerB, "MGRB");
        var boundary = new DateOnly(2026, 9, 11);
        var originalHistory = f.Context.EmployeeEmploymentHistory.Single(x => x.EmployeeId == f.EmployeeId);
        originalHistory.ManagerId = f.ManagerId;
        originalHistory.EffectiveTo = boundary.AddDays(-1);
        f.Context.EmployeeEmploymentHistory.Add(new EmployeeEmploymentHistory
        {
            Id = Guid.NewGuid(),
            TenantId = f.TenantId,
            EmployeeId = f.EmployeeId,
            EffectiveFrom = boundary,
            ManagerId = managerB,
            EmploymentStatus = EmployeeStatus.Active
        });
        await f.Context.SaveChangesAsync();

        var request = (await Service(f).SubmitOnDutyAsync(new(
            boundary.AddDays(-1),
            boundary,
            "spans manager boundary"))).Value!;

        var managerResolver = new EmployeeManagerResolver(f.Context, f.Inner.TenantContext);
        f.Identity.EmployeeId = f.ManagerId;
        var managerA = new AttendanceWorkflowService(f.Context, f.Identity, managerResolver, f.Processor);
        Assert.Equal(ResultStatus.Forbidden, (await managerA.ApproveOnDutyAsync(request.Id)).Status);
        Assert.Equal(AttendanceRequestStatus.Pending, (await f.Context.AttendanceOnDutyRequests.SingleAsync(x => x.Id == request.Id)).Status);

        f.Identity.EmployeeId = managerB;
        var managerBService = new AttendanceWorkflowService(f.Context, f.Identity, managerResolver, f.Processor);
        Assert.Equal(ResultStatus.Forbidden, (await managerBService.ApproveOnDutyAsync(request.Id)).Status);
        Assert.Equal(AttendanceRequestStatus.Pending, (await f.Context.AttendanceOnDutyRequests.SingleAsync(x => x.Id == request.Id)).Status);
    }

    [Fact]
    public async Task Wrong_manager_direct_id_mutations_have_no_side_effects()
    {
        using var f = await FixtureAsync();
        var date = new DateOnly(2026, 9, 10);
        await SeedIncompleteDayAsync(f, date);
        var regularization = (await Service(f).SubmitRegularizationAsync(new(date, AttendanceRegularizationType.MissingOutPunch, null, date.ToDateTime(new(18, 0), DateTimeKind.Utc), "missing"))).Value!;
        var onDuty = (await Service(f).SubmitOnDutyAsync(new(date.AddDays(1), date.AddDays(1), "client visit"))).Value!;
        f.Identity.EmployeeId = Guid.NewGuid();
        var unauthorized = Service(f);
        Assert.Equal(ResultStatus.Forbidden, (await unauthorized.ApproveRegularizationAsync(regularization.Id)).Status);
        Assert.Equal(ResultStatus.Forbidden, (await unauthorized.RejectRegularizationAsync(regularization.Id, "no")).Status);
        Assert.Equal(ResultStatus.Forbidden, (await unauthorized.ApproveOnDutyAsync(onDuty.Id)).Status);
        Assert.Equal(ResultStatus.Forbidden, (await unauthorized.RejectOnDutyAsync(onDuty.Id, "no")).Status);
        Assert.Equal(AttendanceRequestStatus.Pending, (await f.Context.AttendanceRegularizationRequests.SingleAsync(x => x.Id == regularization.Id)).Status);
        Assert.Equal(AttendanceRequestStatus.Pending, (await f.Context.AttendanceOnDutyRequests.SingleAsync(x => x.Id == onDuty.Id)).Status);
        Assert.Single(await f.Context.AttendanceRegularizationEvents.Where(x => x.AttendanceRegularizationRequestId == regularization.Id).ToListAsync());
        Assert.Single(await f.Context.AttendanceOnDutyEvents.Where(x => x.AttendanceOnDutyRequestId == onDuty.Id).ToListAsync());
        Assert.Empty(await f.Context.AttendanceAdjustments.ToListAsync());
        Assert.Empty(await f.Context.EmployeeAttendanceDays.Where(x => x.Status == EmployeeAttendanceDayStatus.OnDuty).ToListAsync());
    }

    private static void AssertPermission(string method, string permission)
    {
        var attribute = typeof(AttendanceWorkflowController).GetMethod(method)!.GetCustomAttributes(typeof(HasPermissionAttribute), false).Cast<HasPermissionAttribute>().Single();
        Assert.Equal(permission, attribute.Permission);
    }

    internal static AttendanceWorkflowService Service(WorkflowFixture f) => Service(f, f.Processor);
    internal static AttendanceWorkflowService Service(WorkflowFixture f, IAttendanceDayProcessor processor) => new(f.Context, f.Identity, f.Managers, processor, new FixedClock(new DateTimeOffset(2026, 10, 20, 20, 0, 0, TimeSpan.Zero)));
    internal static async Task<WorkflowFixture> FixtureAsync()
    {
        var baseFixture = await AttendanceTestFixture.CreateAsync();
        var manager = Guid.NewGuid();
        await baseFixture.AddEmployeeAsync(manager, "MGR");
        var shift = new Shift { Id = Guid.NewGuid(), TenantId = baseFixture.TenantId, ShiftCode = "WF", ShiftName = "Workflow", IsDefault = true, IsActive = true, EffectiveFrom = new(2026, 1, 1), StartTime = new(9, 0), EndTime = new(18, 0), PlannedDurationMinutes = 540, FullDayWorkMinutes = 480, MinimumWorkMinutes = 1, CaptureMode = AttendanceCaptureMode.Mixed };
        baseFixture.Context.Shifts.Add(shift);
        await baseFixture.Context.SaveChangesAsync();
        var employeeUserId = Guid.NewGuid();
        var managerUserId = Guid.NewGuid();
        var identity = new MutableIdentity(baseFixture.TenantId, employeeUserId, baseFixture.EmployeeId, managerUserId, manager);
        baseFixture.Context.Users.Add(new User { Id = identity.UserId, TenantId = baseFixture.TenantId, Email = $"{identity.UserId:N}@example.test", FirstName = "Workflow", LastName = "User", PasswordHash = "test" });
        baseFixture.Context.Users.Add(new User { Id = managerUserId, TenantId = baseFixture.TenantId, Email = $"{managerUserId:N}@example.test", FirstName = "Workflow", LastName = "Manager", PasswordHash = "test" });
        await baseFixture.Context.SaveChangesAsync();
        var managers = new FixedManagerResolver(manager);
        var processor = new AttendanceDayProcessor(baseFixture.Context, baseFixture.TenantContext, baseFixture.CalendarService, new FixedClock(new DateTimeOffset(2026, 10, 10, 20, 0, 0, TimeSpan.Zero)));
        return new WorkflowFixture(baseFixture, manager, managerUserId, identity, managers, processor);
    }

    internal static async Task SeedIncompleteDayAsync(WorkflowFixture f, DateOnly date)
    {
        f.Context.EmployeeAttendanceDays.Add(new EmployeeAttendanceDay { Id = Guid.NewGuid(), TenantId = f.TenantId, EmployeeId = f.EmployeeId, BusinessDate = date, ShiftId = f.ShiftId, ShiftCode = "WF", ExpectedWorkMinutes = 480, RosterAssignmentSource = RosterAssignmentSource.Auto, RosterDayType = RosterDayType.Shift, Status = EmployeeAttendanceDayStatus.Incomplete, HasMissingOutPunch = true });
        await f.Context.SaveChangesAsync();
    }

    private static async Task<Guid> SeedApprovedLeaveAsync(WorkflowFixture f, DateOnly date)
    {
        var typeId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var policyId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var ruleId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var historyId = await f.Context.EmployeeEmploymentHistory.Where(x => x.TenantId == f.TenantId && x.EmployeeId == f.EmployeeId).Select(x => x.Id).SingleAsync();
        f.Context.AddRange(
            new LeaveType { Id = typeId, TenantId = f.TenantId, Code = $"LT-{requestId:N}"[..10], Name = "Test Leave" },
            new LeavePeriod { Id = periodId, TenantId = f.TenantId, Code = $"LP-{requestId:N}"[..10], Name = "Test Period", StartDate = new(2026, 1, 1), EndDate = new(2026, 12, 31) },
            new LeavePolicy { Id = policyId, TenantId = f.TenantId, Code = $"LPL-{requestId:N}"[..11], Name = "Test Policy" },
            new LeavePolicyVersion { Id = versionId, TenantId = f.TenantId, LeavePolicyId = policyId, VersionNumber = 1, EffectiveFrom = new(2026, 1, 1), Status = LeavePolicyVersionStatus.Published },
            new LeavePolicyRule { Id = ruleId, TenantId = f.TenantId, LeavePolicyVersionId = versionId, LeaveTypeId = typeId },
            new LeaveRequest { Id = requestId, TenantId = f.TenantId, EmployeeId = f.EmployeeId, LeaveTypeId = typeId, LeavePeriodId = periodId, LeavePolicyVersionId = versionId, LeavePolicyRuleId = ruleId, EmployeeEmploymentHistoryId = historyId, PolicyGenderSnapshot = Gender.Unspecified, StartDate = date, EndDate = date, RequestedQuantity = 1, ChargeableQuantity = 1, Status = LeaveRequestStatus.Approved, SubmittedAtUtc = DateTime.UtcNow, IdempotencyKey = $"wf-{requestId:N}", PayloadFingerprint = requestId.ToString("N"), Days = [new LeaveRequestDay { Id = Guid.NewGuid(), TenantId = f.TenantId, LeaveRequestId = requestId, Date = date, RequestedQuantity = 1, ChargeableQuantity = 1 }] });
        await f.Context.SaveChangesAsync();
        return requestId;
    }

    internal sealed class WorkflowFixture(AttendanceTestFixture inner, Guid managerId, Guid managerUserId, MutableIdentity identity, FixedManagerResolver managers, IAttendanceDayProcessor processor) : IDisposable
    {
        public AttendanceTestFixture Inner { get; } = inner;
        public HRMS.Infrastructure.Persistence.HrmsDbContext Context => Inner.Context;
        public Guid TenantId => Inner.TenantId;
        public Guid EmployeeId => Inner.EmployeeId;
        public Guid ManagerId { get; } = managerId;
        public Guid ManagerUserId { get; } = managerUserId;
        public Guid RequestId { get; set; }
        public Guid ShiftId => Inner.Context.Shifts.Single(x => x.ShiftCode == "WF").Id;
        public MutableIdentity Identity { get; } = identity;
        public FixedManagerResolver Managers { get; } = managers;
        public IAttendanceDayProcessor Processor { get; } = processor;
        public HRMS.Infrastructure.Persistence.HrmsDbContext CreateContext(Guid tenantId, out TestTenantContext tenant) => Inner.CreateContext(tenantId, out tenant);
        public HRMS.Infrastructure.Persistence.HrmsDbContext CreateIsolatedContext(Guid userId, out TestTenantContext tenant, params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] interceptors)
        {
            tenant = new TestTenantContext(TenantId, userId);
            return Inner.CreateIsolatedContext(tenant, interceptors);
        }
        public AttendanceWorkflowService CreateService(HRMS.Infrastructure.Persistence.HrmsDbContext db, TestTenantContext tenant, Guid userId, Guid employeeId, IEmployeeSerializationLock? serializationLock = null)
        {
            var identity = new MutableIdentity(TenantId, userId, employeeId, ManagerUserId, ManagerId);
            var employment = new EffectiveEmploymentResolver(db, tenant);
            var calendar = new WorkingDayCalendarResolver(db, tenant, employment);
            var foundation = new AttendanceFoundationService(db, tenant, employment, calendar);
            var periodLock = new AttendancePeriodLockService(db, tenant);
            var processor = new AttendanceDayProcessor(db, tenant, foundation, new FixedClock(new DateTimeOffset(2026, 10, 20, 20, 0, 0, TimeSpan.Zero)), periodLock);
            return new AttendanceWorkflowService(db, identity, Managers, processor, new FixedClock(new DateTimeOffset(2026, 10, 20, 20, 0, 0, TimeSpan.Zero)), periodLock, employeeLock: serializationLock);
        }
        public void Dispose() => Inner.Dispose();
    }

    internal sealed class MutableIdentity(Guid tenantId, Guid employeeUserId, Guid employeeId, Guid managerUserId, Guid managerEmployeeId) : IEmployeeIdentityResolver
    {
        public Guid TenantId { get; } = tenantId;
        private Guid employeeId = employeeId;
        public Guid UserId { get; set; } = employeeUserId;
        public Guid EmployeeId
        {
            get => employeeId;
            set
            {
                employeeId = value;
                UserId = value == managerEmployeeId ? managerUserId : employeeUserId;
            }
        }
        public Task<Result<RuntimeEmployeeIdentity>> ResolveCurrentAsync(CancellationToken cancellationToken = default) => Task.FromResult(Result<RuntimeEmployeeIdentity>.Success(new(TenantId, UserId, EmployeeId)));
    }

    internal sealed class FixedManagerResolver(Guid managerId) : IEmployeeManagerResolver
    {
        public Task<Result<EmployeeManagerResolution>> ResolveAsync(Guid employeeId, DateOnly asOfDate, CancellationToken cancellationToken = default) => Task.FromResult(Result<EmployeeManagerResolution>.Success(new(EmployeeManagerResolutionStatus.Resolved, employeeId, managerId, "MGR", "Manager", "test")));
        public Task<bool> WouldCreateCycleAsync(Guid employeeId, Guid proposedManagerId, DateOnly asOfDate, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class FailingProcessor : IAttendanceDayProcessor
    {
        public Task<Result<EmployeeAttendanceDayDto>> ProcessAsync(Guid employeeId, DateOnly businessDate, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Injected processor failure.");
    }

    private sealed class UnlinkedIdentity : IEmployeeIdentityResolver
    {
        public Task<Result<RuntimeEmployeeIdentity>> ResolveCurrentAsync(CancellationToken cancellationToken = default) => Task.FromResult(Result<RuntimeEmployeeIdentity>.NotFound("The authenticated account is not linked to an Employee."));
    }

    private sealed class SelfManagerResolver : IEmployeeManagerResolver
    {
        public Task<Result<EmployeeManagerResolution>> ResolveAsync(Guid employeeId, DateOnly asOfDate, CancellationToken cancellationToken = default) => Task.FromResult(Result<EmployeeManagerResolution>.Success(new(EmployeeManagerResolutionStatus.Resolved, employeeId, employeeId, "SELF", "Self", "test")));
        public Task<bool> WouldCreateCycleAsync(Guid employeeId, Guid proposedManagerId, DateOnly asOfDate, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class DateManagerResolver(Guid historicalManagerId, Guid laterManagerId, DateOnly boundary) : IEmployeeManagerResolver
    {
        public Task<Result<EmployeeManagerResolution>> ResolveAsync(Guid employeeId, DateOnly asOfDate, CancellationToken cancellationToken = default)
        {
            var managerId = asOfDate < boundary ? historicalManagerId : laterManagerId;
            return Task.FromResult(Result<EmployeeManagerResolution>.Success(new(EmployeeManagerResolutionStatus.Resolved, employeeId, managerId, "MGR", "Manager", "effective")));
        }

        public Task<bool> WouldCreateCycleAsync(Guid employeeId, Guid proposedManagerId, DateOnly asOfDate, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }


    private sealed class TestPageQuery : PagedQuery;
}
