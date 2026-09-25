using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.Services;
using HRMS.Domain.Authorization;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

/// <summary>
/// Functional Phase 6E acceptance over the existing Regularization, Attendance
/// processor and monthly snapshot lifecycles. These tests deliberately do not
/// introduce a second attendance calculation path.
/// </summary>
public sealed class AttendanceOperationsMissedPunchEndToEndTests : IClassFixture<HrmsApiFactory>
{
    private readonly HrmsApiFactory factory;
    public AttendanceOperationsMissedPunchEndToEndTests(HrmsApiFactory factory) => this.factory = factory;

    [Fact]
    public async Task Missed_punch_correction_reprocesses_attendance_and_resolves_exception()
    {
        var graph = await new AttendanceHttpEmployeeScenarioBuilder(factory).CreateManagerAsync(
            $"6E-MISSED-{Guid.NewGuid():N}",
            employeePermissions: [Permissions.Attendance.RegularizationRequest, Permissions.Attendance.View],
            managerPermissions: [Permissions.Attendance.RegularizationApprove, Permissions.Attendance.ExceptionView, Permissions.Attendance.MonthlyViewTeam]);
        var date = new DateOnly(2026, 9, 22);
        await SeedIncompleteDayAsync(graph, date);

        var before = await graph.ManagerClient.GetFromJsonAsync<ApiResponse<PagedResult<AttendanceOperationalExceptionDto>>>($"/api/attendance/operations/exceptions?employeeId={graph.EmployeeId}&fromDate={date:yyyy-MM-dd}&toDate={date:yyyy-MM-dd}&exceptionType=MissingOutPunch&page=1&pageSize=10", JsonOptions);
        Assert.Single(before!.Data!.Items);

        using var submit = await graph.EmployeeClient.PostAsJsonAsync("/api/attendance/me/regularizations", new
        {
            businessDate = date,
            requestType = AttendanceRegularizationType.MissingOutPunch,
            proposedOutAtUtc = date.ToDateTime(new(18, 0), DateTimeKind.Utc),
            reason = "Missed OUT punch"
        }, JsonOptions);
        Assert.True(submit.StatusCode == HttpStatusCode.OK, $"{submit.StatusCode}: {await submit.Content.ReadAsStringAsync()}");
        var request = (await submit.Content.ReadFromJsonAsync<ApiResponse<RegularizationDto>>(JsonOptions))!.Data!;

        using var approve = await graph.ManagerClient.PostAsync($"/api/attendance/manager/regularizations/{request.Id}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);

        var after = await graph.ManagerClient.GetFromJsonAsync<ApiResponse<PagedResult<AttendanceOperationalExceptionDto>>>($"/api/attendance/operations/exceptions?employeeId={graph.EmployeeId}&fromDate={date:yyyy-MM-dd}&toDate={date:yyyy-MM-dd}&exceptionType=MissingOutPunch&page=1&pageSize=10", JsonOptions);
        Assert.Empty(after!.Data!.Items);

        await new AttendanceHttpEmployeeScenarioBuilder(factory).ExecuteTenantAsync(graph, async db =>
        {
            var persisted = await db.AttendanceRegularizationRequests.SingleAsync(x => x.Id == request.Id);
            Assert.Equal(AttendanceRequestStatus.Approved, persisted.Status);
            Assert.Single(await db.AttendanceAdjustments.Where(x => x.AttendanceRegularizationRequestId == request.Id).ToListAsync());
            Assert.Equal(2, await db.AttendanceRegularizationEvents.CountAsync(x => x.AttendanceRegularizationRequestId == request.Id));
        });
    }

    [Fact]
    public async Task Regularization_approval_removes_resolved_missed_punch_from_active_exceptions()
    {
        var graph = await new AttendanceHttpEmployeeScenarioBuilder(factory).CreateManagerAsync(
            $"6E-REG-AUTO-{Guid.NewGuid():N}",
            employeePermissions: [Permissions.Attendance.RegularizationRequest, Permissions.Attendance.View],
            managerPermissions: [Permissions.Attendance.RegularizationApprove, Permissions.Attendance.ExceptionView, Permissions.Attendance.MonthlyViewTeam]);
        var date = new DateOnly(2026, 9, 22);
        await SeedIncompleteDayAsync(graph, date);
        var before = await graph.ManagerClient.GetFromJsonAsync<ApiResponse<PagedResult<AttendanceOperationalExceptionDto>>>($"/api/attendance/operations/exceptions?employeeId={graph.EmployeeId}&fromDate={date:yyyy-MM-dd}&toDate={date:yyyy-MM-dd}&exceptionType=MissingOutPunch&page=1&pageSize=10", JsonOptions);
        Assert.Single(before!.Data!.Items);
        var submit = await graph.EmployeeClient.PostAsJsonAsync("/api/attendance/me/regularizations", new { businessDate = date, requestType = AttendanceRegularizationType.MissingOutPunch, proposedOutAtUtc = date.ToDateTime(new(18, 0), DateTimeKind.Utc), reason = "Correct missed OUT" }, JsonOptions);
        var request = (await submit.Content.ReadFromJsonAsync<ApiResponse<RegularizationDto>>(JsonOptions))!.Data!;
        Assert.Equal(HttpStatusCode.OK, submit.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await graph.ManagerClient.PostAsync($"/api/attendance/manager/regularizations/{request.Id}/approve", null)).StatusCode);
        var after = await graph.ManagerClient.GetFromJsonAsync<ApiResponse<PagedResult<AttendanceOperationalExceptionDto>>>($"/api/attendance/operations/exceptions?employeeId={graph.EmployeeId}&fromDate={date:yyyy-MM-dd}&toDate={date:yyyy-MM-dd}&exceptionType=MissingOutPunch&page=1&pageSize=10", JsonOptions);
        Assert.Empty(after!.Data!.Items);
        await new AttendanceHttpEmployeeScenarioBuilder(factory).ExecuteTenantAsync(graph, async db => Assert.Empty(await db.AttendanceExceptionResolutions.ToListAsync()));
    }

    private Task SeedIncompleteDayAsync(AttendanceHttpManagerScenario graph, DateOnly date) =>
        new AttendanceHttpEmployeeScenarioBuilder(factory).ExecuteTenantAsync(graph, async db =>
        {
            db.EmployeeAttendanceDays.Add(new EmployeeAttendanceDay
            {
                Id = Guid.NewGuid(), TenantId = graph.TenantId, EmployeeId = graph.EmployeeId,
                BusinessDate = date, Status = EmployeeAttendanceDayStatus.Incomplete,
                ExpectedWorkMinutes = 480, WorkedMinutes = 0, HasMissingOutPunch = true,
                ProcessedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        });

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
}

public sealed class AttendanceOperationsManualAttendanceEndToEndTests : IClassFixture<HrmsApiFactory>
{
    private readonly HrmsApiFactory factory;
    public AttendanceOperationsManualAttendanceEndToEndTests(HrmsApiFactory factory) => this.factory = factory;

    [Fact]
    public async Task Manual_attendance_maker_checker_reprocesses_authoritative_day()
    {
        var graph = await new AttendanceHttpEmployeeScenarioBuilder(factory).CreateManagerAsync(
            $"6E-MANUAL-{Guid.NewGuid():N}",
            employeePermissions: [Permissions.Attendance.RegularizationRequest, Permissions.Attendance.View],
            managerPermissions: [Permissions.Attendance.RegularizationApprove, Permissions.Attendance.ExceptionView, Permissions.Attendance.MonthlyViewAll]);
        var date = new DateOnly(2026, 9, 23);
        await new AttendanceHttpEmployeeScenarioBuilder(factory).ExecuteTenantAsync(graph, async db =>
        {
            db.AttendancePeriods.Add(new AttendancePeriod { Id = Guid.NewGuid(), TenantId = graph.TenantId, Year = date.Year, Month = date.Month, StartDate = new(date.Year, date.Month, 1), EndDate = new(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month)), Status = AttendancePeriodStatus.Open, DataVersion = 3 });
            db.EmployeeAttendanceDays.Add(new EmployeeAttendanceDay { Id = Guid.NewGuid(), TenantId = graph.TenantId, EmployeeId = graph.EmployeeId, BusinessDate = date, Status = EmployeeAttendanceDayStatus.Absent, ExpectedWorkMinutes = 480, WorkedMinutes = 0, ProcessedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        });

        using var submit = await graph.EmployeeClient.PostAsJsonAsync("/api/attendance/me/regularizations", new
        {
            businessDate = date, requestType = AttendanceRegularizationType.CorrectInOutTime,
            proposedInAtUtc = date.ToDateTime(new(9, 0), DateTimeKind.Utc), proposedOutAtUtc = date.ToDateTime(new(18, 0), DateTimeKind.Utc),
            reason = "Manual attendance"
        }, JsonOptions);
        Assert.True(submit.StatusCode == HttpStatusCode.OK, $"{submit.StatusCode}: {await submit.Content.ReadAsStringAsync()}");
        var request = (await submit.Content.ReadFromJsonAsync<ApiResponse<RegularizationDto>>(JsonOptions))!.Data!;
        using var approve = await graph.ManagerClient.PostAsync($"/api/attendance/manager/regularizations/{request.Id}/approve", null);
        Assert.True(approve.StatusCode == HttpStatusCode.OK, $"{approve.StatusCode}: {await approve.Content.ReadAsStringAsync()}");
        var active = await graph.ManagerClient.GetFromJsonAsync<ApiResponse<PagedResult<AttendanceOperationalExceptionDto>>>($"/api/attendance/operations/exceptions?employeeId={graph.EmployeeId}&fromDate={date:yyyy-MM-dd}&toDate={date:yyyy-MM-dd}&exceptionType=Absent&page=1&pageSize=10", JsonOptions);
        Assert.Empty(active!.Data!.Items);
        var history = await graph.ManagerClient.GetFromJsonAsync<ApiResponse<PagedResult<AttendanceOperationsHistoryItem>>>($"/api/attendance/operations/employees/{graph.EmployeeId}/history?fromDate={date:yyyy-MM-dd}&toDate={date:yyyy-MM-dd}&page=1&pageSize=20", JsonOptions);
        Assert.Contains(history!.Data!.Items, x => x.ReferenceId == request.Id && x.Action == "Regularization:Submitted" && x.ActorUserId == graph.EmployeeUserId);
        Assert.Contains(history.Data.Items, x => x.ReferenceId == request.Id && x.Action == "Regularization:Approved" && x.ActorUserId == graph.ManagerUserId);
        var transition = Assert.Single(history.Data.Items, x => x.ReferenceId == request.Id && x.Action == "RegularizationApproved");
        Assert.Equal(3, transition.OldAttendanceVersion);
        Assert.Equal(4, transition.NewAttendanceVersion);
        Assert.Equal(nameof(EmployeeAttendanceDayStatus.Absent), transition.OldValue);
        Assert.Equal(nameof(EmployeeAttendanceDayStatus.NotProcessed), transition.NewValue);
        using var duplicateApprove = await graph.ManagerClient.PostAsync($"/api/attendance/manager/regularizations/{request.Id}/approve", null);
        Assert.Equal(HttpStatusCode.Conflict, duplicateApprove.StatusCode);

        await new AttendanceHttpEmployeeScenarioBuilder(factory).ExecuteTenantAsync(graph, async db =>
        {
            Assert.Equal(AttendanceRequestStatus.Approved, await db.AttendanceRegularizationRequests.Where(x => x.Id == request.Id).Select(x => x.Status).SingleAsync());
            Assert.Single(await db.AttendanceAdjustments.Where(x => x.AttendanceRegularizationRequestId == request.Id).ToListAsync());
            Assert.Equal(2, await db.AttendanceRegularizationEvents.CountAsync(x => x.AttendanceRegularizationRequestId == request.Id));
        });
    }

    [Fact]
    public async Task Manual_attendance_approval_removes_resolved_exception_from_active_exceptions()
    {
        var graph = await new AttendanceHttpEmployeeScenarioBuilder(factory).CreateManagerAsync(
            $"6E-MANUAL-AUTO-{Guid.NewGuid():N}",
            employeePermissions: [Permissions.Attendance.RegularizationRequest, Permissions.Attendance.View],
            managerPermissions: [Permissions.Attendance.RegularizationApprove, Permissions.Attendance.ExceptionView, Permissions.Attendance.MonthlyViewAll]);
        var date = new DateOnly(2026, 9, 23);
        await new AttendanceHttpEmployeeScenarioBuilder(factory).ExecuteTenantAsync(graph, async db =>
        {
            db.EmployeeAttendanceDays.Add(new EmployeeAttendanceDay { Id = Guid.NewGuid(), TenantId = graph.TenantId, EmployeeId = graph.EmployeeId, BusinessDate = date, Status = EmployeeAttendanceDayStatus.Absent, ExpectedWorkMinutes = 480, WorkedMinutes = 0, ProcessedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        });
        var before = await graph.ManagerClient.GetFromJsonAsync<ApiResponse<PagedResult<AttendanceOperationalExceptionDto>>>($"/api/attendance/operations/exceptions?employeeId={graph.EmployeeId}&fromDate={date:yyyy-MM-dd}&toDate={date:yyyy-MM-dd}&exceptionType=Absent&page=1&pageSize=10", JsonOptions);
        Assert.Single(before!.Data!.Items);
        var submit = await graph.EmployeeClient.PostAsJsonAsync("/api/attendance/me/regularizations", new { businessDate = date, requestType = AttendanceRegularizationType.CorrectInOutTime, proposedInAtUtc = date.ToDateTime(new(9, 0), DateTimeKind.Utc), proposedOutAtUtc = date.ToDateTime(new(18, 0), DateTimeKind.Utc), reason = "Manual attendance correction" }, JsonOptions);
        var request = (await submit.Content.ReadFromJsonAsync<ApiResponse<RegularizationDto>>(JsonOptions))!.Data!;
        Assert.Equal(HttpStatusCode.OK, submit.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await graph.ManagerClient.PostAsync($"/api/attendance/manager/regularizations/{request.Id}/approve", null)).StatusCode);
        var after = await graph.ManagerClient.GetFromJsonAsync<ApiResponse<PagedResult<AttendanceOperationalExceptionDto>>>($"/api/attendance/operations/exceptions?employeeId={graph.EmployeeId}&fromDate={date:yyyy-MM-dd}&toDate={date:yyyy-MM-dd}&exceptionType=Absent&page=1&pageSize=10", JsonOptions);
        Assert.Empty(after!.Data!.Items);
        await new AttendanceHttpEmployeeScenarioBuilder(factory).ExecuteTenantAsync(graph, async db => Assert.Empty(await db.AttendanceExceptionResolutions.ToListAsync()));
    }

    [Fact]
    public async Task Manual_attendance_rejection_keeps_authoritative_day_unchanged_and_is_historic()
    {
        var graph = await new AttendanceHttpEmployeeScenarioBuilder(factory).CreateManagerAsync(
            $"6E-MANUAL-REJECT-{Guid.NewGuid():N}",
            employeePermissions: [Permissions.Attendance.RegularizationRequest, Permissions.Attendance.View],
            managerPermissions: [Permissions.Attendance.RegularizationApprove, Permissions.Attendance.ExceptionView, Permissions.Attendance.MonthlyViewAll]);
        var date = new DateOnly(2026, 9, 23);
        await new AttendanceHttpEmployeeScenarioBuilder(factory).ExecuteTenantAsync(graph, async db =>
        {
            db.EmployeeAttendanceDays.Add(new EmployeeAttendanceDay { Id = Guid.NewGuid(), TenantId = graph.TenantId, EmployeeId = graph.EmployeeId, BusinessDate = date, Status = EmployeeAttendanceDayStatus.Absent, ExpectedWorkMinutes = 480, WorkedMinutes = 0, ProcessedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        });
        var submit = await graph.EmployeeClient.PostAsJsonAsync("/api/attendance/me/regularizations", new { businessDate = date, requestType = AttendanceRegularizationType.CorrectInOutTime, proposedInAtUtc = date.ToDateTime(new(9, 0), DateTimeKind.Utc), proposedOutAtUtc = date.ToDateTime(new(18, 0), DateTimeKind.Utc), reason = "Manual request rejected" }, JsonOptions);
        var request = (await submit.Content.ReadFromJsonAsync<ApiResponse<RegularizationDto>>(JsonOptions))!.Data!;
        Assert.Equal(HttpStatusCode.OK, submit.StatusCode);
        var reject = await graph.ManagerClient.PostAsJsonAsync($"/api/attendance/manager/regularizations/{request.Id}/reject", new { comments = "Evidence insufficient" }, JsonOptions);
        Assert.Equal(HttpStatusCode.OK, reject.StatusCode);
        await new AttendanceHttpEmployeeScenarioBuilder(factory).ExecuteTenantAsync(graph, async db =>
        {
            Assert.Equal(EmployeeAttendanceDayStatus.Absent, await db.EmployeeAttendanceDays.Where(x => x.TenantId == graph.TenantId && x.EmployeeId == graph.EmployeeId && x.BusinessDate == date).Select(x => x.Status).SingleAsync());
            Assert.Empty(await db.AttendanceAdjustments.Where(x => x.TenantId == graph.TenantId).ToListAsync());
            Assert.Contains(await db.AttendanceRegularizationEvents.Where(x => x.AttendanceRegularizationRequestId == request.Id).Select(x => x.EventType).ToListAsync(), x => x == AttendanceRequestEventType.Rejected);
        });
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
}

public sealed class AttendanceOperationsFinalizedPeriodCorrectionEndToEndTests
{
    [Fact]
    public async Task Finalized_period_requires_reopen_and_preserves_payroll_snapshot_history()
    {
        await using var data = await Phase6BAcceptanceData.FinalizedAsync();
        var original = await data.Db.PayrollAttendanceSnapshots.AsNoTracking().SingleAsync(x => x.IsCurrent);
        var period = await data.Db.AttendancePeriods.SingleAsync(x => x.Id == data.PeriodId);

        var closedProcess = new AttendanceMonthlyProcessor(data.Db, data.Scope);
        var reopened = await closedProcess.ReopenAsync(period.Id, new("Phase 6E controlled correction"));
        Assert.True(reopened.Succeeded, reopened.Message);
        Assert.Equal(AttendancePeriodStatus.Open, (await data.Db.AttendancePeriods.SingleAsync(x => x.Id == period.Id)).Status);
        Assert.True((await data.Processor.ProcessAsync(period.Id)).Succeeded);
        Assert.True((await data.Processor.CloseAsync(period.Id)).Succeeded);

        var versions = await data.Db.PayrollAttendanceSnapshots.AsNoTracking().Where(x => x.AttendancePeriodId == period.Id).OrderBy(x => x.Version).ToListAsync();
        Assert.Equal(2, versions.Count);
        Assert.Equal(1, versions[0].Version);
        Assert.Equal(2, versions[1].Version);
        Assert.Equal(original.Id, versions[0].Id);
        Assert.False(versions[0].IsCurrent);
        Assert.True(versions[1].IsCurrent);
        var periodEvents = await data.Db.AttendancePeriodEvents.AsNoTracking().Where(x => x.AttendancePeriodId == period.Id).Select(x => x.EventType).ToListAsync();
        Assert.Contains(AttendancePeriodEventType.Reopened, periodEvents);
        Assert.Contains(AttendancePeriodEventType.PayrollSnapshotSuperseded, periodEvents);
    }
}
