using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public static class AttendanceOperationsProviderAcceptance
{
    public static async Task RunAsync(HrmsDbContext db, IAttendanceOperationsProviderFixture fixture)
    {
        await RunLeaveAndOnDutyAcceptanceAsync(db, fixture);
        await RunAcceptanceAsync(db, fixture);
    }

    private static async Task RunLeaveAndOnDutyAcceptanceAsync(HrmsDbContext db, IAttendanceOperationsProviderFixture fixture)
    {
        var employeeTenant = fixture.EmployeeTenant;
        var managerTenant = fixture.ManagerTenant;
        var clock = new FixedClock(new DateTimeOffset(2026, 10, 20, 20, 0, 0, TimeSpan.Zero));
        var employment = new EffectiveEmploymentResolver(db, employeeTenant);
        var calendar = new WorkingDayCalendarResolver(db, employeeTenant, employment);
        var foundation = new AttendanceFoundationService(db, employeeTenant, employment, calendar);
        var processor = new AttendanceDayProcessor(db, employeeTenant, foundation, clock);
        db.Shifts.Add(new Shift
        {
            Id = Guid.NewGuid(), TenantId = fixture.TenantId, ShiftCode = $"P6E-{Guid.NewGuid():N}"[..12],
            ShiftName = "Phase 6E provider", IsDefault = true, IsActive = true, EffectiveFrom = new(2026, 1, 1),
            StartTime = new(9, 0), EndTime = new(18, 0), PlannedDurationMinutes = 540,
            FullDayWorkMinutes = 480, MinimumWorkMinutes = 1, CaptureMode = AttendanceCaptureMode.Mixed
        });
        await db.SaveChangesAsync();

        var leaveDate = new DateOnly(2026, 10, 10);
        var absentLeave = await processor.ProcessAsync(fixture.EmployeeId, leaveDate);
        Assert.True(absentLeave.Succeeded, absentLeave.Message);
        Assert.Equal(EmployeeAttendanceDayStatus.Absent, absentLeave.Value!.Status);
        var operations = new AttendanceOperationsService(db, managerTenant);
        var leaveBefore = await operations.GetOperationalExceptionsAsync(new()
        {
            FromDate = leaveDate, ToDate = leaveDate, EmployeeId = fixture.EmployeeId,
            ExceptionType = AttendanceExceptionType.Absent, IsResolved = false, Page = 1, PageSize = 10
        });
        Assert.Single(leaveBefore.Value!.Items);

        var leaveIdentity = new EmployeeIdentityResolver(db, employeeTenant);
        var leaveValidation = new LeaveRequestValidationService(db, leaveIdentity, employment,
            new LeavePeriodResolver(db, employeeTenant), new LeavePolicyResolver(db, employment, employeeTenant), calendar);
        var accounting = new LeaveBalanceAccountingService(db, employeeTenant, TimeProvider.System);
        var submission = new LeaveRequestSubmissionService(db, leaveIdentity, leaveValidation,
            fixture.CreateLeaveSubmissionLock(db), TimeProvider.System, balanceAccountingService: accounting);
        var submittedLeave = await submission.SubmitAsync(new(fixture.LeaveTypeId, leaveDate, leaveDate, "Phase 6E MySQL Leave integration"));
        Assert.True(submittedLeave.Succeeded, submittedLeave.Message);

        var managerIdentity = new EmployeeIdentityResolver(db, managerTenant);
        var approval = new LeaveRequestApprovalService(db, managerIdentity, new EmployeeManagerResolver(db, managerTenant),
            fixture.CreateLeaveSubmissionLock(db), clock, balanceAccountingService: accounting, attendanceProcessor: processor);
        var approvedLeave = await approval.ApproveAsync(submittedLeave.Value!.RequestId);
        Assert.True(approvedLeave.Succeeded, approvedLeave.Message);
        Assert.Equal(LeaveRequestStatus.Approved, approvedLeave.Value!.Status);
        Assert.Equal(EmployeeAttendanceDayStatus.OnLeave, await db.EmployeeAttendanceDays
            .Where(x => x.TenantId == fixture.TenantId && x.EmployeeId == fixture.EmployeeId && x.BusinessDate == leaveDate)
            .Select(x => x.Status).SingleAsync());
        var leaveAfter = await operations.GetOperationalExceptionsAsync(new()
        {
            FromDate = leaveDate, ToDate = leaveDate, EmployeeId = fixture.EmployeeId,
            ExceptionType = AttendanceExceptionType.Absent, IsResolved = false, Page = 1, PageSize = 10
        });
        Assert.Empty(leaveAfter.Value!.Items);

        var dutyDate = new DateOnly(2026, 10, 12);
        var absentDuty = await processor.ProcessAsync(fixture.EmployeeId, dutyDate);
        Assert.True(absentDuty.Succeeded, absentDuty.Message);
        Assert.Equal(EmployeeAttendanceDayStatus.Absent, absentDuty.Value!.Status);
        var dutyBefore = await operations.GetOperationalExceptionsAsync(new()
        {
            FromDate = dutyDate, ToDate = dutyDate, EmployeeId = fixture.EmployeeId,
            ExceptionType = AttendanceExceptionType.Absent, IsResolved = false, Page = 1, PageSize = 10
        });
        Assert.Single(dutyBefore.Value!.Items);

        var managers = new EmployeeManagerResolver(db, employeeTenant);
        var employeeWorkflow = new AttendanceWorkflowService(db, leaveIdentity, managers, processor, clock);
        var request = await employeeWorkflow.SubmitOnDutyAsync(new(dutyDate, dutyDate, "Phase 6E MySQL On Duty integration"));
        Assert.True(request.Succeeded, request.Message);
        var managerWorkflow = new AttendanceWorkflowService(db, managerIdentity, managers, processor, clock);
        var approvedDuty = await managerWorkflow.ApproveOnDutyAsync(request.Value!.Id);
        Assert.True(approvedDuty.Succeeded, approvedDuty.Message);
        Assert.Equal(EmployeeAttendanceDayStatus.OnDuty, await db.EmployeeAttendanceDays
            .Where(x => x.TenantId == fixture.TenantId && x.EmployeeId == fixture.EmployeeId && x.BusinessDate == dutyDate)
            .Select(x => x.Status).SingleAsync());
        var dutyAfter = await operations.GetOperationalExceptionsAsync(new()
        {
            FromDate = dutyDate, ToDate = dutyDate, EmployeeId = fixture.EmployeeId,
            ExceptionType = AttendanceExceptionType.Absent, IsResolved = false, Page = 1, PageSize = 10
        });
        Assert.Empty(dutyAfter.Value!.Items);
        Assert.Empty(await db.AttendanceExceptionResolutions.Where(x => x.TenantId == fixture.TenantId &&
            x.EmployeeId == fixture.EmployeeId && (x.AttendanceDayId == absentLeave.Value.Id || x.AttendanceDayId == absentDuty.Value.Id)).ToListAsync());
    }

    private static async Task RunAcceptanceAsync(HrmsDbContext db, IAttendanceOperationsProviderFixture fixture)
    {
        var tenant = fixture.ManagerTenant;
        const int year = 2026;
        const int month = 12;
        var periodService = new AttendanceMonthlyProcessor(db, tenant);
        var created = await periodService.CreatePeriodAsync(new(year, month));
        Assert.True(created.Succeeded, created.Message);
        var period = created.Value!;

        var lateDate = new DateOnly(year, month, 5);
        var days = Enumerable.Range(1, DateTime.DaysInMonth(year, month))
            .Select(day => new EmployeeAttendanceDay
            {
                Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId,
                BusinessDate = new(year, month, day), Status = EmployeeAttendanceDayStatus.Present,
                ExpectedWorkMinutes = 480, WorkedMinutes = 480, ProcessedAtUtc = DateTime.UtcNow
            }).ToList();
        days.AddRange(Enumerable.Range(1, DateTime.DaysInMonth(year, month)).Select(day => new EmployeeAttendanceDay
        {
            Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.ManagerId,
            BusinessDate = new(year, month, day), Status = EmployeeAttendanceDayStatus.Present,
            ExpectedWorkMinutes = 480, WorkedMinutes = 480, ProcessedAtUtc = DateTime.UtcNow
        }));
        var lateDay = days.Single(x => x.EmployeeId == fixture.EmployeeId && x.BusinessDate == lateDate);
        lateDay.IsLateIn = true;
        lateDay.ScheduledStartUtc = new DateTime(year, month, 5, 9, 0, 0, DateTimeKind.Utc);
        lateDay.FirstPunchAtUtc = new DateTime(year, month, 5, 9, 15, 0, DateTimeKind.Utc);
        db.EmployeeAttendanceDays.AddRange(days);
        await db.SaveChangesAsync();

        var operations = new AttendanceOperationsService(db, tenant);
        var before = await operations.GetOperationalExceptionsAsync(new()
        {
            FromDate = lateDate, ToDate = lateDate, EmployeeId = fixture.EmployeeId,
            ExceptionType = AttendanceExceptionType.LateArrival, IsResolved = false, Page = 1, PageSize = 10
        });
        Assert.True(before.Succeeded, before.Message);
        var late = Assert.Single(before.Value!.Items);
        Assert.Equal(1, late.AttendanceVersion);

        var resolved = await operations.ResolveExceptionAsync(new(late.Id, AttendanceExceptionType.LateArrival, "Acknowledge", "Provider acceptance", 1));
        Assert.True(resolved.Succeeded, resolved.Message);
        Assert.Equal(1, await db.AttendanceExceptionResolutions.CountAsync(x => x.TenantId == fixture.TenantId && x.AttendanceDayId == late.Id));
        Assert.Equal(1, await db.EmployeeAuditLogs.CountAsync(x => x.TenantId == fixture.TenantId && x.RecordId == late.Id && x.FieldName == "LateArrival:Acknowledge"));
        await Assert.ThrowsAsync<DbUpdateException>(() => operations.ResolveExceptionAsync(new(late.Id,
            AttendanceExceptionType.LateArrival, "Acknowledge", "Duplicate provider event", 1)));
        db.ChangeTracker.Clear();
        Assert.Equal(1, await db.AttendanceExceptionResolutions.CountAsync(x => x.TenantId == fixture.TenantId && x.AttendanceDayId == late.Id));
        var hiddenV1 = await operations.GetOperationalExceptionsAsync(new()
        {
            FromDate = lateDate, ToDate = lateDate, EmployeeId = fixture.EmployeeId,
            ExceptionType = AttendanceExceptionType.LateArrival, IsResolved = false, Page = 1, PageSize = 10
        });
        Assert.Empty(hiddenV1.Value!.Items);

        var process = await periodService.ProcessAsync(period.Id);
        Assert.True(process.Succeeded, process.Message);
        var closePreview = await periodService.GetClosePreviewAsync(period.Id);
        Assert.True(closePreview.Succeeded && closePreview.Value!.CanClose, string.Join(" | ", closePreview.Value?.Blockers ?? []));
        var close = await periodService.CloseAsync(period.Id);
        Assert.True(close.Succeeded, close.Message);
        var versionOne = await db.PayrollAttendanceSnapshots.AsNoTracking()
            .Where(x => x.AttendancePeriodId == period.Id && x.EmployeeId == fixture.EmployeeId && x.IsCurrent)
            .Select(x => x.Version).SingleAsync();
        Assert.Equal(1, versionOne);

        var locked = await operations.SubmitManualAttendanceAsync(new(
            fixture.EmployeeId, new(year, month, 6), AttendanceRegularizationType.CorrectInOutTime,
            new DateTime(year, month, 6, 9, 0, 0, DateTimeKind.Utc), new DateTime(year, month, 6, 18, 0, 0, DateTimeKind.Utc),
            "locked provider test", null, period.DataVersion));
        Assert.Equal(ResultStatus.Conflict, locked.Status);

        var reopen = await periodService.ReopenAsync(period.Id, new($"{fixture.ProviderName} Phase 6E provider acceptance"));
        Assert.True(reopen.Succeeded, reopen.Message);
        Assert.Equal(2, reopen.Value!.DataVersion);
        var visibleV2 = await operations.GetOperationalExceptionsAsync(new()
        {
            FromDate = lateDate, ToDate = lateDate, EmployeeId = fixture.EmployeeId,
            ExceptionType = AttendanceExceptionType.LateArrival, IsResolved = false, Page = 1, PageSize = 10
        });
        Assert.Single(visibleV2.Value!.Items);
        Assert.Equal(2, visibleV2.Value.Items[0].AttendanceVersion);

        var reprocess = await periodService.ProcessAsync(period.Id);
        Assert.True(reprocess.Succeeded, reprocess.Message);
        var refinalized = await periodService.CloseAsync(period.Id);
        Assert.True(refinalized.Succeeded, refinalized.Message);
        Assert.Equal(1, await db.PayrollAttendanceSnapshots.CountAsync(x => x.AttendancePeriodId == period.Id && x.EmployeeId == fixture.EmployeeId && x.IsCurrent));
        Assert.Equal(1, await db.PayrollAttendanceSnapshots.CountAsync(x => x.AttendancePeriodId == period.Id && x.EmployeeId == fixture.EmployeeId && x.Version == 1));
        Assert.Equal(1, await db.PayrollAttendanceSnapshots.CountAsync(x => x.AttendancePeriodId == period.Id && x.EmployeeId == fixture.EmployeeId && x.Version == 2));

        var nextMonth = await periodService.CreatePeriodAsync(new(2027, 1));
        Assert.True(nextMonth.Succeeded, nextMonth.Message);
        var nextMonthDays = Enumerable.Range(1, DateTime.DaysInMonth(2027, 1)).Select(day => new EmployeeAttendanceDay
        {
            Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId,
            BusinessDate = new(2027, 1, day), Status = EmployeeAttendanceDayStatus.Present,
            ExpectedWorkMinutes = 480, WorkedMinutes = 480, ProcessedAtUtc = DateTime.UtcNow
        });
        db.EmployeeAttendanceDays.AddRange(nextMonthDays);
        await db.SaveChangesAsync();

        var employeeOperations = new AttendanceOperationsService(db, fixture.EmployeeTenant);
        var manual = await employeeOperations.SubmitManualAttendanceAsync(new(
            fixture.EmployeeId, new(2027, 1, 6), AttendanceRegularizationType.CorrectInOutTime,
            new DateTime(2027, 1, 6, 9, 0, 0, DateTimeKind.Utc), new DateTime(2027, 1, 6, 18, 0, 0, DateTimeKind.Utc),
            "manual provider request", fixture.ProviderName, 1));
        Assert.True(manual.Succeeded, manual.Message);
        Assert.Equal(AttendanceRequestStatus.Pending, await db.AttendanceRegularizationRequests
            .Where(x => x.Id == manual.Value!.Id).Select(x => x.Status).SingleAsync());
        Assert.Equal(1, await db.AttendanceRegularizationEvents.CountAsync(x => x.AttendanceRegularizationRequestId == manual.Value!.Id));

        var employment = new EffectiveEmploymentResolver(db, fixture.ManagerTenant);
        var calendar = new WorkingDayCalendarResolver(db, fixture.ManagerTenant, employment);
        var dayProcessor = new AttendanceDayProcessor(db, fixture.ManagerTenant,
            new AttendanceFoundationService(db, fixture.ManagerTenant, employment, calendar),
            new FixedClock(new DateTimeOffset(2027, 1, 20, 20, 0, 0, TimeSpan.Zero)),
            new AttendancePeriodLockService(db, fixture.ManagerTenant));
        var workflow = new AttendanceWorkflowService(db, new EmployeeIdentityResolver(db, fixture.ManagerTenant),
            new EmployeeManagerResolver(db, fixture.ManagerTenant), dayProcessor,
            new FixedClock(new DateTimeOffset(2027, 1, 20, 20, 0, 0, TimeSpan.Zero)),
            new AttendancePeriodLockService(db, fixture.ManagerTenant));
        var selfApproval = await new AttendanceWorkflowService(db, new EmployeeIdentityResolver(db, fixture.EmployeeTenant),
            new EmployeeManagerResolver(db, fixture.EmployeeTenant), dayProcessor,
            new FixedClock(new DateTimeOffset(2027, 1, 20, 20, 0, 0, TimeSpan.Zero)),
            new AttendancePeriodLockService(db, fixture.EmployeeTenant)).ApproveRegularizationAsync(manual.Value!.Id);
        Assert.False(selfApproval.Succeeded);
        Assert.Equal(AttendanceRequestStatus.Pending, await db.AttendanceRegularizationRequests
            .Where(x => x.Id == manual.Value.Id).Select(x => x.Status).SingleAsync());
        Assert.Empty(await db.AttendanceAdjustments.Where(x => x.AttendanceRegularizationRequestId == manual.Value.Id).ToListAsync());
        var approved = await workflow.ApproveRegularizationAsync(manual.Value.Id);
        Assert.True(approved.Succeeded, approved.Message);
        Assert.Equal(AttendanceRequestStatus.Approved, approved.Value!.Status);
        Assert.Single(await db.AttendanceAdjustments.Where(x => x.AttendanceRegularizationRequestId == manual.Value.Id).ToListAsync());
        Assert.Equal(2, await db.AttendanceRegularizationEvents.CountAsync(x => x.AttendanceRegularizationRequestId == manual.Value.Id));
        Assert.Equal(2, await db.EmployeeAuditLogs.CountAsync(x => x.TenantId == fixture.TenantId && x.ImportBatchId == manual.Value.Id));

        var stale = await operations.ApplyBulkCorrectionsAsync(new[]
        {
            new AttendanceBulkCorrectionItem(fixture.EmployeeId, new(2027, 1, 7), AttendanceRegularizationType.CorrectInOutTime,
                new DateTime(2027, 1, 7, 9, 0, 0, DateTimeKind.Utc), new DateTime(2027, 1, 7, 18, 0, 0, DateTimeKind.Utc), "stale", 1)
        });
        Assert.True(stale.Succeeded, stale.Message);
        Assert.Equal("StaleVersion", Assert.Single(stale.Value!.Items).FailureCode);

        var bulk = await operations.ApplyBulkCorrectionsAsync(new[]
        {
            new AttendanceBulkCorrectionItem(fixture.EmployeeId, new(2027, 1, 8), AttendanceRegularizationType.CorrectInOutTime,
                new DateTime(2027, 1, 8, 9, 0, 0, DateTimeKind.Utc), new DateTime(2027, 1, 8, 18, 0, 0, DateTimeKind.Utc), "bulk", 2)
        });
        Assert.True(bulk.Succeeded, bulk.Message);
        Assert.Equal((1, 0), (bulk.Value!.Succeeded, bulk.Value.Failed));
        Assert.NotNull(Assert.Single(bulk.Value.Items).RequestId);

        var otherTenantOperations = new AttendanceOperationsService(db, new TestTenantContext(fixture.OtherTenantId, fixture.ManagerUserId));
        var otherTenant = await otherTenantOperations.GetOperationalExceptionsAsync(new()
        {
            FromDate = lateDate, ToDate = lateDate, ExceptionType = AttendanceExceptionType.LateArrival,
            IsResolved = false, Page = 1, PageSize = 10
        });
        Assert.True(otherTenant.Succeeded, otherTenant.Message);
        Assert.Empty(otherTenant.Value!.Items);
        Assert.Empty(await db.AttendanceExceptionResolutions.AsNoTracking()
            .Where(x => x.TenantId == fixture.OtherTenantId && x.AttendanceDayId == late.Id).ToListAsync());
        var otherHistory = await otherTenantOperations.GetHistoryAsync(fixture.EmployeeId, lateDate, lateDate,
            new AttendanceOperationsHistoryQuery { Page = 1, PageSize = 10 });
        Assert.True(otherHistory.Succeeded, otherHistory.Message);
        Assert.Empty(otherHistory.Value!.Items);
        var requestCountBeforeCrossTenantMutation = await db.AttendanceRegularizationRequests.CountAsync(x => x.TenantId == fixture.TenantId);
        var crossTenantMutation = await otherTenantOperations.SubmitManualAttendanceAsync(new(
            fixture.EmployeeId, new(2027, 1, 9), AttendanceRegularizationType.CorrectInOutTime,
            new DateTime(2027, 1, 9, 9, 0, 0, DateTimeKind.Utc), new DateTime(2027, 1, 9, 18, 0, 0, DateTimeKind.Utc),
            "cross-tenant provider attempt", null, 1));
        Assert.False(crossTenantMutation.Succeeded);
        Assert.Equal(requestCountBeforeCrossTenantMutation, await db.AttendanceRegularizationRequests.CountAsync(x => x.TenantId == fixture.TenantId));

        Assert.Equal(1, await db.PayrollAttendanceSnapshots.CountAsync(x => x.AttendancePeriodId == period.Id && x.EmployeeId == fixture.EmployeeId && x.Version == 1));
        Assert.Equal(1, await db.PayrollAttendanceSnapshots.CountAsync(x => x.AttendancePeriodId == period.Id && x.EmployeeId == fixture.EmployeeId && x.IsCurrent));
        Assert.Equal(1, await db.AttendanceExceptionResolutions.CountAsync(x => x.TenantId == fixture.TenantId && x.AttendanceDayId == late.Id && x.AttendanceVersion == 1));
    }
}

public interface IAttendanceOperationsProviderFixture
{
    string ProviderName { get; }
    Guid TenantId { get; }
    Guid OtherTenantId { get; }
    Guid EmployeeId { get; }
    Guid ManagerId { get; }
    Guid ManagerUserId { get; }
    Guid LeaveTypeId { get; }
    TestTenantContext EmployeeTenant { get; }
    TestTenantContext ManagerTenant { get; }
    HRMS.Application.Abstractions.ILeaveRequestSubmissionLock CreateLeaveSubmissionLock(HrmsDbContext db);
}
