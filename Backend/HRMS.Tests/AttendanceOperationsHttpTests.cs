using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using HRMS.Application.Common;
using HRMS.Application.Abstractions;
using HRMS.Domain.Authorization;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HRMS.Tests;

public sealed class AttendanceOperationsHttpTests : IClassFixture<HrmsApiFactory>
{
    private readonly HrmsApiFactory factory;

    public AttendanceOperationsHttpTests(HrmsApiFactory factory) => this.factory = factory;

    [Fact]
    public async Task Employee_can_view_own_exceptions_but_not_operational_scope()
    {
        var scenario = await new AttendanceHttpEmployeeScenarioBuilder(factory).CreateAsync(
            "OPS", [Permissions.Attendance.View]);
        await SeedDayAsync(scenario.Host, scenario.TenantId, scenario.EmployeeId, new DateOnly(2026, 9, 20));

        var self = await scenario.EmployeeClient.GetAsync("/api/attendance/me/exceptions?page=1&pageSize=10");
        var operations = await scenario.EmployeeClient.GetAsync("/api/attendance/operations/exceptions?page=1&pageSize=10");

        Assert.Equal(HttpStatusCode.OK, self.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, operations.StatusCode);
    }

    [Fact]
    public async Task Manager_operational_query_returns_team_only()
    {
        var scenario = await new AttendanceHttpEmployeeScenarioBuilder(factory).CreateManagerAsync(
            "OPS-MGR", managerPermissions: [Permissions.Attendance.ExceptionView, Permissions.Attendance.MonthlyViewAll]);
        var team = await new AttendanceHttpEmployeeScenarioBuilder(factory).AddLinkedEmployeeAsync(scenario, "Team");
        await SeedDayAsync(scenario.Host, scenario.TenantId, team.EmployeeId, new DateOnly(2026, 9, 20));
        var outside = await new AttendanceHttpEmployeeScenarioBuilder(factory).CreateAsync("OPS-OUTSIDE");
        await SeedDayAsync(outside.Host, outside.TenantId, outside.EmployeeId, new DateOnly(2026, 9, 20));

        var response = await scenario.ManagerClient.GetFromJsonAsync<ApiResponse<PagedResult<AttendanceOperationalExceptionDto>>>(
            "/api/attendance/operations/exceptions?page=1&pageSize=100", JsonOptions);

        Assert.NotNull(response?.Data);
        Assert.Contains(response!.Data!.Items, item => item.EmployeeId == team.EmployeeId);
        Assert.DoesNotContain(response.Data.Items, item => item.EmployeeId == outside.EmployeeId);
    }

    [Fact]
    public async Task Manager_operational_query_excludes_same_tenant_non_team_employee()
    {
        var builder = new AttendanceHttpEmployeeScenarioBuilder(factory);
        var manager = await builder.CreateManagerAsync("OPS-MGR-NONTEAM", managerPermissions: [Permissions.Attendance.ExceptionView, Permissions.Attendance.MonthlyViewTeam]);
        var team = await builder.AddLinkedEmployeeAsync(manager, "Team");
        var nonTeamEmployeeId = Guid.NewGuid();
        await factory.ExecuteInTenantScopeAsync(manager.Host, async services =>
        {
            var db = services.GetRequiredService<HRMS.Infrastructure.Persistence.HrmsDbContext>();
            db.Employees.Add(new Employee { Id = nonTeamEmployeeId, TenantId = manager.TenantId, EmployeeCode = "OUTSIDE-TEAM", FirstName = "Outside", LastName = "Team", DateOfJoining = new DateOnly(2026, 1, 1) });
            db.EmployeeEmploymentHistory.Add(new EmployeeEmploymentHistory { Id = Guid.NewGuid(), TenantId = manager.TenantId, EmployeeId = nonTeamEmployeeId, EffectiveFrom = new DateOnly(2026, 1, 1), ManagerId = null, EmploymentStatus = EmployeeStatus.Active, CreatedBy = "attendance-http-test" });
            await db.SaveChangesAsync();
        });
        await SeedDayAsync(manager.Host, manager.TenantId, team.EmployeeId, new DateOnly(2026, 9, 20));
        await SeedDayAsync(manager.Host, manager.TenantId, nonTeamEmployeeId, new DateOnly(2026, 9, 20));

        var response = await manager.ManagerClient.GetFromJsonAsync<ApiResponse<PagedResult<AttendanceOperationalExceptionDto>>>("/api/attendance/operations/exceptions?fromDate=2026-09-20&toDate=2026-09-20&page=1&pageSize=100", JsonOptions);

        Assert.Contains(response!.Data!.Items, item => item.EmployeeId == team.EmployeeId);
        Assert.DoesNotContain(response.Data.Items, item => item.EmployeeId == nonTeamEmployeeId);
    }

    [Fact]
    public async Task Cross_tenant_operational_query_returns_no_data()
    {
        var first = await new AttendanceHttpEmployeeScenarioBuilder(factory).CreateAsync(
            "OPS-TENANT-A", [Permissions.Attendance.ExceptionView]);
        var second = await new AttendanceHttpEmployeeScenarioBuilder(factory).CreateAsync(
            "OPS-TENANT-B", [Permissions.Attendance.ExceptionView]);
        await SeedDayAsync(second.Host, second.TenantId, second.EmployeeId, new DateOnly(2026, 9, 20));

        var response = await first.EmployeeClient.GetFromJsonAsync<ApiResponse<PagedResult<AttendanceOperationalExceptionDto>>>(
            $"/api/attendance/operations/exceptions?employeeId={second.EmployeeId}&page=1&pageSize=10", JsonOptions);

        Assert.NotNull(response?.Data);
        Assert.Empty(response!.Data!.Items);
    }

    [Fact]
    public async Task Operational_query_pages_and_filters_server_side_contract()
    {
        var scenario = await new AttendanceHttpEmployeeScenarioBuilder(factory).CreateAsync(
            "OPS-PAGE", [Permissions.Attendance.ExceptionView, Permissions.Attendance.MonthlyViewAll]);
        await SeedDayAsync(scenario.Host, scenario.TenantId, scenario.EmployeeId, new DateOnly(2026, 9, 20));
        await SeedDayAsync(scenario.Host, scenario.TenantId, scenario.EmployeeId, new DateOnly(2026, 9, 21));

        var response = await scenario.EmployeeClient.GetFromJsonAsync<ApiResponse<PagedResult<AttendanceOperationalExceptionDto>>>(
            "/api/attendance/operations/exceptions?fromDate=2026-09-21&toDate=2026-09-21&page=1&pageSize=1", JsonOptions);

        Assert.NotNull(response?.Data);
        Assert.Equal(1, response!.Data!.TotalCount);
        Assert.Single(response.Data.Items);
        Assert.Equal(new DateOnly(2026, 9, 21), response.Data.Items[0].BusinessDate);
    }

    [Fact]
    public async Task Missing_in_punch_exception_is_derived_from_authoritative_attendance()
    {
        var scenario = await CorrectionOperatorAsync("OPS-MISSING-IN");
        var date = new DateOnly(2026, 9, 22);
        await factory.ExecuteInTenantScopeAsync(scenario.Host, async services =>
        {
            var db = services.GetRequiredService<HRMS.Infrastructure.Persistence.HrmsDbContext>();
            db.EmployeeAttendanceDays.Add(new EmployeeAttendanceDay { Id = Guid.NewGuid(), TenantId = scenario.TenantId, EmployeeId = scenario.EmployeeId, BusinessDate = date, Status = EmployeeAttendanceDayStatus.Incomplete, HasMissingInPunch = true, ExpectedWorkMinutes = 480, ProcessedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        });
        var result = await scenario.EmployeeClient.GetFromJsonAsync<ApiResponse<PagedResult<AttendanceOperationalExceptionDto>>>($"/api/attendance/operations/exceptions?fromDate={date:yyyy-MM-dd}&toDate={date:yyyy-MM-dd}&exceptionType=MissingInPunch&page=1&pageSize=10", JsonOptions);
        Assert.Single(result!.Data!.Items);
        Assert.Equal(AttendanceExceptionType.MissingInPunch, result.Data.Items[0].ExceptionType);
    }

    [Fact]
    public async Task Manual_attendance_request_uses_existing_regularization_maker_checker_path()
    {
        var scenario = await new AttendanceHttpEmployeeScenarioBuilder(factory).CreateAsync(
            "OPS-MANUAL", [Permissions.Attendance.AdminCorrectionManage, Permissions.Attendance.MonthlyViewAll]);
        var response = await scenario.EmployeeClient.PostAsJsonAsync("/api/attendance/operations/manual", new
        {
            employeeId = scenario.EmployeeId,
            businessDate = "2026-09-22",
            correctionType = "CorrectInOutTime",
            proposedInAtUtc = "2026-09-22T09:00:00Z",
            proposedOutAtUtc = "2026-09-22T17:00:00Z",
            reason = "Approved operational manual attendance",
            comments = "Maker submission",
            expectedAttendanceVersion = 1
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<RegularizationDto>>(JsonOptions);
        Assert.Equal(AttendanceRequestStatus.Pending, payload!.Data!.Status);
        await scenario.EmployeeClient.GetAsync("/api/attendance/me/exceptions?page=1&pageSize=10");
    }

    [Fact]
    public async Task Bulk_attendance_correction_applies_valid_items()
    {
        var scenario = await CorrectionOperatorAsync("OPS-BULK-VALID");
        var response = await scenario.EmployeeClient.PostAsJsonAsync("/api/attendance/operations/bulk-corrections", new[] { BulkItem(scenario, 1) }, JsonOptions);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<ApiResponse<AttendanceBulkCorrectionResponse>>(JsonOptions))!.Data!;
        Assert.Equal(1, result.Succeeded);
        Assert.True(result.Items[0].Success);
        await scenario.EmployeeClient.GetAsync("/api/attendance/me/exceptions?page=1&pageSize=10");
    }

    [Fact]
    public async Task Bulk_attendance_correction_returns_stale_item_conflict()
    {
        var scenario = await CorrectionOperatorAsync("OPS-BULK-STALE");
        await SeedDayAsync(scenario.Host, scenario.TenantId, scenario.EmployeeId, new DateOnly(2026, 9, 22));
        var response = await scenario.EmployeeClient.PostAsJsonAsync("/api/attendance/operations/bulk-corrections", new[] { BulkItem(scenario, 2) }, JsonOptions);
        var result = (await response.Content.ReadFromJsonAsync<ApiResponse<AttendanceBulkCorrectionResponse>>(JsonOptions))!.Data!;
        Assert.Equal("StaleVersion", result.Items.Single().FailureCode);
        Assert.Equal(0, result.Succeeded);
        await factory.ExecuteInTenantScopeAsync(scenario.Host, async services =>
        {
            var db = services.GetRequiredService<HRMS.Infrastructure.Persistence.HrmsDbContext>();
            Assert.Empty(await db.AttendanceRegularizationRequests.Where(x => x.TenantId == scenario.TenantId && x.EmployeeId == scenario.EmployeeId).ToListAsync());
            Assert.Equal(EmployeeAttendanceDayStatus.Absent, await db.EmployeeAttendanceDays.Where(x => x.TenantId == scenario.TenantId && x.EmployeeId == scenario.EmployeeId).Select(x => x.Status).SingleAsync());
            Assert.Empty(await db.AttendanceAdjustments.Where(x => x.TenantId == scenario.TenantId && x.EmployeeId == scenario.EmployeeId).ToListAsync());
        });
    }

    [Fact]
    public async Task Bulk_attendance_correction_blocks_locked_period()
    {
        var scenario = await CorrectionOperatorAsync("OPS-BULK-LOCKED");
        await scenario.Host.ExecuteTenantForTest(factory, scenario.TenantId, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), AttendancePeriodStatus.Closed);
        var response = await scenario.EmployeeClient.PostAsJsonAsync("/api/attendance/operations/bulk-corrections", new[] { BulkItem(scenario, 1) }, JsonOptions);
        var result = (await response.Content.ReadFromJsonAsync<ApiResponse<AttendanceBulkCorrectionResponse>>(JsonOptions))!.Data!;
        Assert.Equal("LockedPeriod", result.Items.Single().FailureCode);
    }

    [Fact]
    public async Task Bulk_attendance_correction_respects_employee_scope()
    {
        var scenario = await CorrectionOperatorAsync("OPS-BULK-SCOPE");
        var foreign = await new AttendanceHttpEmployeeScenarioBuilder(factory).CreateAsync("OPS-BULK-FOREIGN");
        var item = new AttendanceBulkCorrectionItem(foreign.EmployeeId, new(2026, 9, 22), AttendanceRegularizationType.CorrectInOutTime, new DateTime(2026, 9, 22, 9, 0, 0, DateTimeKind.Utc), new DateTime(2026, 9, 22, 18, 0, 0, DateTimeKind.Utc), "scope test", 1);
        var response = await scenario.EmployeeClient.PostAsJsonAsync("/api/attendance/operations/bulk-corrections", new[] { item }, JsonOptions);
        var result = (await response.Content.ReadFromJsonAsync<ApiResponse<AttendanceBulkCorrectionResponse>>(JsonOptions))!.Data!;
        Assert.Equal("Unauthorized", result.Items.Single().FailureCode);
        await foreign.Host.ExecuteTenantForTest(factory, foreign.TenantId, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), null);
    }

    [Fact]
    public async Task Bulk_attendance_correction_returns_explicit_per_item_results()
    {
        var scenario = await CorrectionOperatorAsync("OPS-BULK-PER-ITEM");
        var response = await scenario.EmployeeClient.PostAsJsonAsync("/api/attendance/operations/bulk-corrections", new[] { BulkItem(scenario, 1), BulkItem(scenario, 2) }, JsonOptions);
        var result = (await response.Content.ReadFromJsonAsync<ApiResponse<AttendanceBulkCorrectionResponse>>(JsonOptions))!.Data!;
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(1, result.Succeeded);
        Assert.Equal(1, result.Failed);
    }

    [Fact]
    public async Task Bulk_attendance_correction_limit_is_enforced()
    {
        var scenario = await CorrectionOperatorAsync("OPS-BULK-LIMIT");
        var items = Enumerable.Range(0, 101).Select(_ => BulkItem(scenario, 1)).ToArray();
        var response = await scenario.EmployeeClient.PostAsJsonAsync("/api/attendance/operations/bulk-corrections", items, JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Bulk_duplicate_item_is_idempotent()
    {
        var scenario = await CorrectionOperatorAsync("OPS-BULK-DUPLICATE");
        var item = BulkItem(scenario, 1);
        var response = await scenario.EmployeeClient.PostAsJsonAsync("/api/attendance/operations/bulk-corrections", new[] { item, item }, JsonOptions);
        var result = (await response.Content.ReadFromJsonAsync<ApiResponse<AttendanceBulkCorrectionResponse>>(JsonOptions))!.Data!;
        Assert.Equal(2, result.Succeeded);
        Assert.Equal(result.Items[0].RequestId, result.Items[1].RequestId);
        await factory.ExecuteInTenantScopeAsync(scenario.Host, async services =>
        {
            var db = services.GetRequiredService<HRMS.Infrastructure.Persistence.HrmsDbContext>();
            Assert.Single(await db.AttendanceRegularizationRequests.Where(x => x.TenantId == scenario.TenantId && x.EmployeeId == scenario.EmployeeId).ToListAsync());
            Assert.Single(await db.AttendanceRegularizationEvents.Where(x => x.TenantId == scenario.TenantId).ToListAsync());
        });
    }

    [Fact]
    public async Task Bulk_successful_items_create_audit_history()
    {
        var scenario = await CorrectionOperatorAsync("OPS-BULK-AUDIT");
        var response = await scenario.EmployeeClient.PostAsJsonAsync("/api/attendance/operations/bulk-corrections", new[] { BulkItem(scenario, 1) }, JsonOptions);
        var result = (await response.Content.ReadFromJsonAsync<ApiResponse<AttendanceBulkCorrectionResponse>>(JsonOptions))!.Data!;
        var requestId = result.Items.Single().RequestId;
        Assert.NotNull(requestId);
        var history = await scenario.EmployeeClient.GetFromJsonAsync<ApiResponse<PagedResult<AttendanceOperationsHistoryItem>>>($"/api/attendance/operations/employees/{scenario.EmployeeId}/history?fromDate=2026-09-22&toDate=2026-09-22&page=1&pageSize=20", JsonOptions);
        Assert.Contains(history!.Data!.Items, x => x.ReferenceId == requestId && x.Action == "ManualAttendanceRequested" && x.AttendanceVersion == 1);
    }

    [Fact]
    public async Task Dashboard_exception_counts_match_operational_drilldown()
    {
        var scenario = await CorrectionOperatorAsync("OPS-DASHBOARD-DRILL");
        var date = new DateOnly(2026, 9, 22);
        await factory.ExecuteInTenantScopeAsync(scenario.Host, async services =>
        {
            var db = services.GetRequiredService<HRMS.Infrastructure.Persistence.HrmsDbContext>();
            db.EmployeeAttendanceDays.AddRange(
                new EmployeeAttendanceDay { Id = Guid.NewGuid(), TenantId = scenario.TenantId, EmployeeId = scenario.EmployeeId, BusinessDate = date, Status = EmployeeAttendanceDayStatus.Present, IsLateIn = true, ProcessedAtUtc = DateTime.UtcNow },
                new EmployeeAttendanceDay { Id = Guid.NewGuid(), TenantId = scenario.TenantId, EmployeeId = scenario.EmployeeId, BusinessDate = date.AddDays(-1), Status = EmployeeAttendanceDayStatus.Present, IsEarlyOut = true, ProcessedAtUtc = DateTime.UtcNow },
                new EmployeeAttendanceDay { Id = Guid.NewGuid(), TenantId = scenario.TenantId, EmployeeId = scenario.EmployeeId, BusinessDate = date.AddDays(-2), Status = EmployeeAttendanceDayStatus.Absent, ProcessedAtUtc = DateTime.UtcNow },
                new EmployeeAttendanceDay { Id = Guid.NewGuid(), TenantId = scenario.TenantId, EmployeeId = scenario.EmployeeId, BusinessDate = date.AddDays(-3), Status = EmployeeAttendanceDayStatus.Incomplete, HasMissingInPunch = true, ProcessedAtUtc = DateTime.UtcNow },
                new EmployeeAttendanceDay { Id = Guid.NewGuid(), TenantId = scenario.TenantId, EmployeeId = scenario.EmployeeId, BusinessDate = date.AddDays(-4), Status = EmployeeAttendanceDayStatus.Incomplete, HasMissingOutPunch = true, ProcessedAtUtc = DateTime.UtcNow });
            db.AttendanceRegularizationRequests.Add(new AttendanceRegularizationRequest { Id = Guid.NewGuid(), TenantId = scenario.TenantId, EmployeeId = scenario.EmployeeId, BusinessDate = date, RequestType = AttendanceRegularizationType.CorrectInOutTime, Reason = "pending correction", SubmittedByUserId = scenario.UserId, SubmittedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        });
        var dashboard = await scenario.EmployeeClient.GetFromJsonAsync<ApiResponse<AttendanceOperationsDashboardDto>>("/api/attendance/operations/dashboard?fromDate=2026-09-18&toDate=2026-09-22", JsonOptions);
        Assert.NotNull(dashboard?.Data);
        Assert.Equal(1, dashboard!.Data!.LateDays);
        Assert.Equal(1, dashboard.Data.EarlyDepartureDays);
        Assert.Equal(1, dashboard.Data.ActiveAbsentExceptions);
        Assert.Equal(2, dashboard.Data.MissedPunchExceptions);
        Assert.Equal(dashboard.Data.LateDays, await OperationalCount(scenario, "LateArrival", "2026-09-18", "2026-09-22"));
        Assert.Equal(dashboard.Data.EarlyDepartureDays, await OperationalCount(scenario, "EarlyDeparture", "2026-09-18", "2026-09-22"));
        Assert.Equal(dashboard.Data.ActiveAbsentExceptions, await OperationalCount(scenario, "Absent", "2026-09-18", "2026-09-22"));
        Assert.Equal(dashboard.Data.MissingInPunchExceptions, await OperationalCount(scenario, "MissingInPunch", "2026-09-18", "2026-09-22"));
        Assert.Equal(dashboard.Data.MissingOutPunchExceptions, await OperationalCount(scenario, "MissingOutPunch", "2026-09-18", "2026-09-22"));
    }

    [Fact]
    public async Task Dashboard_pending_corrections_drilldown_matches_scoped_date_filtered_queue()
    {
        var builder = new AttendanceHttpEmployeeScenarioBuilder(factory);
        var graph = await builder.CreateManagerAsync("OPS-DASHBOARD-PENDING", managerPermissions: [Permissions.Attendance.ExceptionView, Permissions.Attendance.RegularizationApprove, Permissions.Attendance.MonthlyViewTeam]);
        var team = await builder.AddLinkedEmployeeAsync(graph, "PendingCorrection");
        await factory.ExecuteInTenantScopeAsync(graph.Host, async services =>
        {
            var db = services.GetRequiredService<HRMS.Infrastructure.Persistence.HrmsDbContext>();
            db.AttendanceRegularizationRequests.AddRange(
                new AttendanceRegularizationRequest { Id = Guid.NewGuid(), TenantId = graph.TenantId, EmployeeId = team.EmployeeId, BusinessDate = new(2026, 9, 22), RequestType = AttendanceRegularizationType.CorrectInOutTime, Reason = "In period", SubmittedByUserId = team.UserId, SubmittedAtUtc = DateTime.UtcNow },
                new AttendanceRegularizationRequest { Id = Guid.NewGuid(), TenantId = graph.TenantId, EmployeeId = team.EmployeeId, BusinessDate = new(2026, 8, 31), RequestType = AttendanceRegularizationType.CorrectInOutTime, Reason = "Outside period", SubmittedByUserId = team.UserId, SubmittedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        });
        var dashboard = await graph.ManagerClient.GetFromJsonAsync<ApiResponse<AttendanceOperationsDashboardDto>>("/api/attendance/operations/dashboard?fromDate=2026-09-22&toDate=2026-09-22", JsonOptions);
        var queue = await graph.ManagerClient.GetFromJsonAsync<ApiResponse<PagedResult<RegularizationDto>>>("/api/attendance/manager/regularizations?fromDate=2026-09-22&toDate=2026-09-22&page=1&pageSize=25", JsonOptions);
        Assert.Equal(1, dashboard!.Data!.PendingCorrections);
        Assert.Equal(dashboard.Data.PendingCorrections, queue!.Data!.TotalCount);
        Assert.All(queue.Data.Items, item => Assert.Equal(new DateOnly(2026, 9, 22), item.BusinessDate));
    }

    [Fact]
    public async Task Operational_exception_export_is_filtered_and_reads_bounded_workbench_pages()
    {
        var scenario = await new AttendanceHttpEmployeeScenarioBuilder(factory).CreateAsync("OPS-EXPORT-PAGED", [Permissions.Attendance.ExceptionView, Permissions.Attendance.ReportExport, Permissions.Attendance.MonthlyViewAll]);
        await SeedDayAsync(scenario.Host, scenario.TenantId, scenario.EmployeeId, new DateOnly(2026, 9, 20));
        await SeedDayAsync(scenario.Host, scenario.TenantId, scenario.EmployeeId, new DateOnly(2026, 9, 21));
        var response = await scenario.EmployeeClient.GetAsync("/api/attendance/operations/exceptions/export?fromDate=2026-09-20&toDate=2026-09-20&exceptionType=Absent");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var csv = await response.Content.ReadAsStringAsync();
        Assert.Contains("2026-09-20", csv);
        Assert.DoesNotContain("2026-09-21", csv);
        Assert.DoesNotContain(scenario.EmployeeId.ToString(), csv);
    }

    [Fact]
    public async Task Operational_exception_export_respects_tenant_scope()
    {
        var first = await new AttendanceHttpEmployeeScenarioBuilder(factory).CreateAsync(
            "OPS-EXPORT-TENANT-A", [Permissions.Attendance.ExceptionView, Permissions.Attendance.ReportExport, Permissions.Attendance.MonthlyViewAll]);
        var second = await new AttendanceHttpEmployeeScenarioBuilder(factory).CreateAsync(
            "OPS-EXPORT-TENANT-B", [Permissions.Attendance.ExceptionView, Permissions.Attendance.ReportExport, Permissions.Attendance.MonthlyViewAll]);
        await SeedDayAsync(second.Host, second.TenantId, second.EmployeeId, new DateOnly(2026, 9, 20));

        var response = await first.EmployeeClient.GetAsync($"/api/attendance/operations/exceptions/export?employeeId={second.EmployeeId}&fromDate=2026-09-20&toDate=2026-09-20");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var csv = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(second.EmployeeCode, csv);
        Assert.Equal(1, csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public async Task Bulk_regularization_approval_is_audited_and_reprocesses()
    {
        var graph = await new AttendanceHttpEmployeeScenarioBuilder(factory).CreateManagerAsync("OPS-BULK-APPROVE", managerPermissions: [Permissions.Attendance.RegularizationApprove, Permissions.Attendance.ExceptionView, Permissions.Attendance.MonthlyViewAll]);
        var date = new DateOnly(2026, 9, 22);
        var requestId = Guid.NewGuid();
        await factory.ExecuteInTenantScopeAsync(graph.Host, async services =>
        {
            var db = services.GetRequiredService<HRMS.Infrastructure.Persistence.HrmsDbContext>();
            db.EmployeeAttendanceDays.Add(new EmployeeAttendanceDay { Id = Guid.NewGuid(), TenantId = graph.TenantId, EmployeeId = graph.EmployeeId, BusinessDate = date, Status = EmployeeAttendanceDayStatus.Absent, ExpectedWorkMinutes = 480, ProcessedAtUtc = DateTime.UtcNow });
            db.AttendanceRegularizationRequests.Add(new AttendanceRegularizationRequest { Id = requestId, TenantId = graph.TenantId, EmployeeId = graph.EmployeeId, BusinessDate = date, RequestType = AttendanceRegularizationType.CorrectInOutTime, ProposedInAtUtc = date.ToDateTime(new(9, 0), DateTimeKind.Utc), ProposedOutAtUtc = date.ToDateTime(new(18, 0), DateTimeKind.Utc), Reason = "Bulk approve correction", SubmittedByUserId = graph.EmployeeUserId, SubmittedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        });
        var response = await graph.ManagerClient.PostAsJsonAsync("/api/attendance/operations/bulk", new[] { new AttendanceBulkActionItem(requestId, false, true, 1, "Bulk approval") }, JsonOptions);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<ApiResponse<AttendanceBulkActionResponse>>(JsonOptions))!.Data!;
        Assert.True(result.Items.Single().Success);
        var history = await graph.ManagerClient.GetFromJsonAsync<ApiResponse<PagedResult<AttendanceOperationsHistoryItem>>>($"/api/attendance/operations/employees/{graph.EmployeeId}/history?fromDate={date:yyyy-MM-dd}&toDate={date:yyyy-MM-dd}&page=1&pageSize=20", JsonOptions);
        var transition = Assert.Single(history!.Data!.Items, x => x.ReferenceId == requestId && x.Action == "RegularizationApproved");
        Assert.Equal(1, transition.OldAttendanceVersion);
        Assert.Equal(1, transition.NewAttendanceVersion);
        await factory.ExecuteInTenantScopeAsync(graph.Host, async services =>
        {
            var db = services.GetRequiredService<HRMS.Infrastructure.Persistence.HrmsDbContext>();
            Assert.Equal(AttendanceRequestStatus.Approved, await db.AttendanceRegularizationRequests.Where(x => x.Id == requestId).Select(x => x.Status).SingleAsync());
            Assert.Single(await db.AttendanceAdjustments.Where(x => x.AttendanceRegularizationRequestId == requestId).ToListAsync());
            Assert.Contains(await db.AttendanceRegularizationEvents.Where(x => x.AttendanceRegularizationRequestId == requestId).Select(x => x.EventType).ToListAsync(), x => x == AttendanceRequestEventType.Approved);
        });
    }

    [Fact]
    public async Task Bulk_regularization_rejection_is_audited_without_attendance_mutation()
    {
        var graph = await new AttendanceHttpEmployeeScenarioBuilder(factory).CreateManagerAsync("OPS-BULK-REJECT", managerPermissions: [Permissions.Attendance.RegularizationApprove, Permissions.Attendance.ExceptionView, Permissions.Attendance.MonthlyViewAll]);
        var date = new DateOnly(2026, 9, 22);
        var requestId = Guid.NewGuid();
        await factory.ExecuteInTenantScopeAsync(graph.Host, async services =>
        {
            var db = services.GetRequiredService<HRMS.Infrastructure.Persistence.HrmsDbContext>();
            db.EmployeeAttendanceDays.Add(new EmployeeAttendanceDay { Id = Guid.NewGuid(), TenantId = graph.TenantId, EmployeeId = graph.EmployeeId, BusinessDate = date, Status = EmployeeAttendanceDayStatus.Absent, ExpectedWorkMinutes = 480, ProcessedAtUtc = DateTime.UtcNow });
            db.AttendanceRegularizationRequests.Add(new AttendanceRegularizationRequest { Id = requestId, TenantId = graph.TenantId, EmployeeId = graph.EmployeeId, BusinessDate = date, RequestType = AttendanceRegularizationType.CorrectInOutTime, ProposedInAtUtc = date.ToDateTime(new(9, 0), DateTimeKind.Utc), ProposedOutAtUtc = date.ToDateTime(new(18, 0), DateTimeKind.Utc), Reason = "Bulk reject correction", SubmittedByUserId = graph.EmployeeUserId, SubmittedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        });
        var response = await graph.ManagerClient.PostAsJsonAsync("/api/attendance/operations/bulk", new[] { new AttendanceBulkActionItem(requestId, false, false, 1, "Reject unsupported evidence") }, JsonOptions);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<ApiResponse<AttendanceBulkActionResponse>>(JsonOptions))!.Data!;
        Assert.True(result.Items.Single().Success);
        await factory.ExecuteInTenantScopeAsync(graph.Host, async services =>
        {
            var db = services.GetRequiredService<HRMS.Infrastructure.Persistence.HrmsDbContext>();
            Assert.Equal(AttendanceRequestStatus.Rejected, await db.AttendanceRegularizationRequests.Where(x => x.Id == requestId).Select(x => x.Status).SingleAsync());
            Assert.Empty(await db.AttendanceAdjustments.Where(x => x.AttendanceRegularizationRequestId == requestId).ToListAsync());
            Assert.Contains(await db.AttendanceRegularizationEvents.Where(x => x.AttendanceRegularizationRequestId == requestId).Select(x => x.EventType).ToListAsync(), x => x == AttendanceRequestEventType.Rejected);
        });
    }

    private async Task<int> OperationalCount(AttendanceHttpEmployeeScenario scenario, string type, string from, string to)
    {
        var result = await scenario.EmployeeClient.GetFromJsonAsync<ApiResponse<PagedResult<AttendanceOperationalExceptionDto>>>($"/api/attendance/operations/exceptions?fromDate={from}&toDate={to}&exceptionType={type}&isResolved=false&page=1&pageSize=1", JsonOptions);
        return result!.Data!.TotalCount;
    }

    [Fact]
    public async Task Late_resolution_removes_matching_exception_from_active_query()
    {
        var scenario = await CorrectionOperatorAsync("OPS-LATE-RESOLUTION");
        var date = new DateOnly(2026, 9, 22);
        var dayId = Guid.NewGuid();
        await factory.ExecuteInTenantScopeAsync(scenario.Host, async services =>
        {
            var db = services.GetRequiredService<HRMS.Infrastructure.Persistence.HrmsDbContext>();
            db.AttendancePeriods.Add(new AttendancePeriod { Id = Guid.NewGuid(), TenantId = scenario.TenantId, Year = 2026, Month = 9, StartDate = new(2026, 9, 1), EndDate = new(2026, 9, 30), DataVersion = 1 });
            db.EmployeeAttendanceDays.Add(new EmployeeAttendanceDay { Id = dayId, TenantId = scenario.TenantId, EmployeeId = scenario.EmployeeId, BusinessDate = date, Status = EmployeeAttendanceDayStatus.Present, IsLateIn = true, ScheduledStartUtc = date.ToDateTime(new(9, 0), DateTimeKind.Utc), FirstPunchAtUtc = date.ToDateTime(new(9, 15), DateTimeKind.Utc), ProcessedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        });
        var resolution = await scenario.EmployeeClient.PostAsJsonAsync("/api/attendance/operations/exceptions/resolve", new AttendanceExceptionResolutionRequest(dayId, AttendanceExceptionType.LateArrival, "Acknowledge", "Reviewed by operations", 1), JsonOptions);
        Assert.Equal(HttpStatusCode.OK, resolution.StatusCode);
        await factory.ExecuteInTenantScopeAsync(scenario.Host, async services =>
        {
            var db = services.GetRequiredService<HRMS.Infrastructure.Persistence.HrmsDbContext>();
            var audit = Assert.Single(await db.EmployeeAuditLogs.Where(x => x.RecordId == dayId && x.Module == "Attendance").ToListAsync());
            Assert.Equal(scenario.TenantId, audit.TenantId);
            Assert.Equal("Operations", audit.Section);
            Assert.Equal("EmployeeAttendanceDay", audit.EntityName);
            var resolutionRow = Assert.Single(await db.AttendanceExceptionResolutions.Where(x => x.AttendanceDayId == dayId).ToListAsync());
            Assert.Equal(scenario.TenantId, resolutionRow.TenantId);
            Assert.Equal(scenario.EmployeeId, resolutionRow.EmployeeId);
            Assert.Equal(1, resolutionRow.AttendanceVersion);
            Assert.Equal(AttendanceExceptionType.LateArrival, resolutionRow.ExceptionType);
            Assert.Equal(AttendanceExceptionResolutionAction.Acknowledge, resolutionRow.Action);
            var activeSql = db.EmployeeAttendanceDays.AsNoTracking().Where(day => day.TenantId == scenario.TenantId && day.EmployeeId == scenario.EmployeeId && day.Id == dayId && day.IsLateIn && !db.AttendanceExceptionResolutions.Any(r =>
                r.TenantId == day.TenantId && r.EmployeeId == day.EmployeeId && r.AttendanceDayId == day.Id &&
                r.AttendanceVersion == db.AttendancePeriods.Where(p => p.TenantId == day.TenantId && p.StartDate <= day.BusinessDate && p.EndDate >= day.BusinessDate).Select(p => (int?)p.DataVersion).FirstOrDefault() &&
                r.ExceptionType == AttendanceExceptionType.LateArrival && (r.Action == AttendanceExceptionResolutionAction.Acknowledge || r.Action == AttendanceExceptionResolutionAction.Waive))).ToQueryString();
            Assert.Contains("NOT EXISTS", activeSql, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("AttendanceVersion", activeSql, StringComparison.Ordinal);
            Assert.Contains("ExceptionType", activeSql, StringComparison.Ordinal);
            Assert.Contains("TenantId", activeSql, StringComparison.Ordinal);
        });
        var active = await scenario.EmployeeClient.GetFromJsonAsync<ApiResponse<PagedResult<AttendanceOperationalExceptionDto>>>("/api/attendance/operations/exceptions?fromDate=2026-09-22&toDate=2026-09-22&exceptionType=LateArrival&isResolved=false&page=1&pageSize=10", JsonOptions);
        Assert.Empty(active!.Data!.Items);
        await factory.ExecuteInTenantScopeAsync(scenario.Host, async services =>
        {
            var db = services.GetRequiredService<HRMS.Infrastructure.Persistence.HrmsDbContext>();
            var audit = Assert.Single(await db.EmployeeAuditLogs.Where(x => x.RecordId == dayId && x.Module == "Attendance").ToListAsync());
            Assert.Equal("Reviewed by operations", audit.Reason);
            Assert.Equal("LateArrival:Acknowledge", audit.FieldName);
        });
    }

    [Fact]
    public async Task Late_resolution_remains_visible_in_history()
    {
        var scenario = await CorrectionOperatorAsync("OPS-LATE-HISTORY");
        var date = new DateOnly(2026, 9, 22);
        var dayId = await SeedLateDayAsync(scenario, date);
        var response = await scenario.EmployeeClient.PostAsJsonAsync("/api/attendance/operations/exceptions/resolve", new AttendanceExceptionResolutionRequest(dayId, AttendanceExceptionType.LateArrival, "Acknowledge", "Late reviewed", 1), JsonOptions);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var historyResponse = await scenario.EmployeeClient.GetAsync($"/api/attendance/operations/employees/{scenario.EmployeeId}/history?fromDate=2026-09-22&toDate=2026-09-22&page=1&pageSize=10");
        Assert.True(historyResponse.StatusCode == HttpStatusCode.OK, await historyResponse.Content.ReadAsStringAsync());
        var history = await historyResponse.Content.ReadFromJsonAsync<ApiResponse<PagedResult<AttendanceOperationsHistoryItem>>>(JsonOptions);
        var item = Assert.Single(history!.Data!.Items);
        Assert.Equal(dayId, item.AttendanceDayId);
        Assert.Equal("LateArrival:Acknowledge", item.Action);
        Assert.Equal("Late reviewed", item.Reason);
        Assert.Equal(scenario.UserId, item.ActorUserId);
        Assert.Equal(1, item.AttendanceVersion);
    }

    [Fact]
    public async Task Resolved_old_attendance_version_does_not_hide_new_version_exception()
    {
        var scenario = await CorrectionOperatorAsync("OPS-LATE-VERSION");
        var date = new DateOnly(2026, 9, 22);
        var dayId = await SeedLateDayAsync(scenario, date);
        var response = await scenario.EmployeeClient.PostAsJsonAsync("/api/attendance/operations/exceptions/resolve", new AttendanceExceptionResolutionRequest(dayId, AttendanceExceptionType.LateArrival, "Acknowledge", "V1 reviewed", 1), JsonOptions);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await factory.ExecuteInTenantScopeAsync(scenario.Host, async services =>
        {
            var db = services.GetRequiredService<HRMS.Infrastructure.Persistence.HrmsDbContext>();
            var period = await db.AttendancePeriods.SingleAsync(x => x.TenantId == scenario.TenantId && x.Month == 9 && x.Year == 2026);
            period.DataVersion = 2;
            await db.SaveChangesAsync();
        });
        var activeResponse = await scenario.EmployeeClient.GetAsync($"/api/attendance/operations/exceptions?employeeId={scenario.EmployeeId}&fromDate=2026-09-22&toDate=2026-09-22&exceptionType=LateArrival&isResolved=false&page=1&pageSize=10");
        Assert.True(activeResponse.StatusCode == HttpStatusCode.OK, await activeResponse.Content.ReadAsStringAsync());
        var active = await activeResponse.Content.ReadFromJsonAsync<ApiResponse<PagedResult<AttendanceOperationalExceptionDto>>>(JsonOptions);
        var lateV2 = Assert.Single(active!.Data!.Items);
        Assert.Equal(2, lateV2.AttendanceVersion);
        var history = await scenario.EmployeeClient.GetFromJsonAsync<ApiResponse<PagedResult<AttendanceOperationsHistoryItem>>>($"/api/attendance/operations/employees/{scenario.EmployeeId}/history?fromDate=2026-09-22&toDate=2026-09-22&page=1&pageSize=10", JsonOptions);
        Assert.Contains(history!.Data!.Items, x => x.AttendanceDayId == dayId && x.Action == "LateArrival:Acknowledge");
        Assert.Contains(history.Data.Items, x => x.AttendanceDayId == dayId && x.AttendanceVersion == 1);
    }

    [Fact]
    public async Task Early_resolution_removes_matching_exception_from_active_query()
    {
        var scenario = await CorrectionOperatorAsync("OPS-EARLY-RESOLUTION");
        var dayId = await SeedEarlyDayAsync(scenario, new DateOnly(2026, 9, 22));
        var before = await scenario.EmployeeClient.GetFromJsonAsync<ApiResponse<PagedResult<AttendanceOperationalExceptionDto>>>("/api/attendance/operations/exceptions?fromDate=2026-09-22&toDate=2026-09-22&exceptionType=EarlyDeparture&page=1&pageSize=10", JsonOptions);
        Assert.Single(before!.Data!.Items);
        var resolution = await scenario.EmployeeClient.PostAsJsonAsync("/api/attendance/operations/exceptions/resolve", new AttendanceExceptionResolutionRequest(dayId, AttendanceExceptionType.EarlyDeparture, "Acknowledge", "Reviewed early departure", 1), JsonOptions);
        Assert.Equal(HttpStatusCode.OK, resolution.StatusCode);
        var active = await scenario.EmployeeClient.GetFromJsonAsync<ApiResponse<PagedResult<AttendanceOperationalExceptionDto>>>("/api/attendance/operations/exceptions?fromDate=2026-09-22&toDate=2026-09-22&exceptionType=EarlyDeparture&page=1&pageSize=10", JsonOptions);
        Assert.Empty(active!.Data!.Items);
        var history = await scenario.EmployeeClient.GetFromJsonAsync<ApiResponse<PagedResult<AttendanceOperationsHistoryItem>>>($"/api/attendance/operations/employees/{scenario.EmployeeId}/history?fromDate=2026-09-22&toDate=2026-09-22&page=1&pageSize=10", JsonOptions);
        Assert.Contains(history!.Data!.Items, x => x.AttendanceDayId == dayId && x.Action == "EarlyDeparture:Acknowledge");
    }

    [Fact]
    public async Task Early_resolution_remains_visible_in_history()
    {
        var scenario = await CorrectionOperatorAsync("OPS-EARLY-HISTORY");
        var dayId = await SeedEarlyDayAsync(scenario, new DateOnly(2026, 9, 22));
        var response = await scenario.EmployeeClient.PostAsJsonAsync("/api/attendance/operations/exceptions/resolve", new AttendanceExceptionResolutionRequest(dayId, AttendanceExceptionType.EarlyDeparture, "Waive", "Approved early waiver", 1), JsonOptions);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var history = await scenario.EmployeeClient.GetFromJsonAsync<ApiResponse<PagedResult<AttendanceOperationsHistoryItem>>>($"/api/attendance/operations/employees/{scenario.EmployeeId}/history?fromDate=2026-09-22&toDate=2026-09-22&page=1&pageSize=10", JsonOptions);
        var item = Assert.Single(history!.Data!.Items);
        Assert.Equal(dayId, item.AttendanceDayId);
        Assert.Equal("EarlyDeparture:Waive", item.Action);
        Assert.Equal("Approved early waiver", item.Reason);
        Assert.Equal(1, item.AttendanceVersion);
    }

    [Fact]
    public async Task Resolved_old_early_version_does_not_hide_new_version_exception()
    {
        var scenario = await CorrectionOperatorAsync("OPS-EARLY-VERSION");
        var dayId = await SeedEarlyDayAsync(scenario, new DateOnly(2026, 9, 22));
        var response = await scenario.EmployeeClient.PostAsJsonAsync("/api/attendance/operations/exceptions/resolve", new AttendanceExceptionResolutionRequest(dayId, AttendanceExceptionType.EarlyDeparture, "Acknowledge", "V1 early reviewed", 1), JsonOptions);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await factory.ExecuteInTenantScopeAsync(scenario.Host, async services =>
        {
            var db = services.GetRequiredService<HRMS.Infrastructure.Persistence.HrmsDbContext>();
            var period = await db.AttendancePeriods.SingleAsync(x => x.TenantId == scenario.TenantId && x.Month == 9 && x.Year == 2026);
            period.DataVersion = 2;
            await db.SaveChangesAsync();
        });
        var active = await scenario.EmployeeClient.GetFromJsonAsync<ApiResponse<PagedResult<AttendanceOperationalExceptionDto>>>($"/api/attendance/operations/exceptions?employeeId={scenario.EmployeeId}&fromDate=2026-09-22&toDate=2026-09-22&exceptionType=EarlyDeparture&page=1&pageSize=10", JsonOptions);
        var v2 = Assert.Single(active!.Data!.Items);
        Assert.Equal(2, v2.AttendanceVersion);
        var history = await scenario.EmployeeClient.GetFromJsonAsync<ApiResponse<PagedResult<AttendanceOperationsHistoryItem>>>($"/api/attendance/operations/employees/{scenario.EmployeeId}/history?fromDate=2026-09-22&toDate=2026-09-22&page=1&pageSize=10", JsonOptions);
        Assert.Contains(history!.Data!.Items, x => x.AttendanceDayId == dayId && x.AttendanceVersion == 1);
    }

    [Fact]
    public async Task Resolved_exception_is_removed_from_manager_and_employee_active_inboxes()
    {
        var builder = new AttendanceHttpEmployeeScenarioBuilder(factory);
        var manager = await builder.CreateManagerAsync("OPS-INBOX-RESOLVED",
            employeePermissions: [Permissions.Attendance.View],
            managerPermissions: [Permissions.Attendance.ExceptionView, Permissions.Attendance.MonthlyViewTeam]);
        var employee = new AttendanceHttpEmployeeScenario(manager.TenantId, manager.TenantCode, manager.Host, manager.EmployeeUserId, manager.EmployeeId, "team-employee", manager.EmployeeClient);
        var dayId = await SeedEarlyDayAsync(employee, new DateOnly(2026, 9, 22));
        var before = await manager.ManagerClient.GetFromJsonAsync<ApiResponse<PagedResult<AttendanceOperationalExceptionDto>>>("/api/attendance/operations/exceptions?fromDate=2026-09-22&toDate=2026-09-22&exceptionType=EarlyDeparture&page=1&pageSize=10", JsonOptions);
        Assert.Contains(before!.Data!.Items, x => x.EmployeeId == manager.EmployeeId);
        var resolution = await manager.ManagerClient.PostAsJsonAsync("/api/attendance/operations/exceptions/resolve", new AttendanceExceptionResolutionRequest(dayId, AttendanceExceptionType.EarlyDeparture, "Acknowledge", "Manager reviewed", 1), JsonOptions);
        Assert.Equal(HttpStatusCode.OK, resolution.StatusCode);
        var managerActive = await manager.ManagerClient.GetFromJsonAsync<ApiResponse<PagedResult<AttendanceOperationalExceptionDto>>>("/api/attendance/operations/exceptions?fromDate=2026-09-22&toDate=2026-09-22&exceptionType=EarlyDeparture&page=1&pageSize=10", JsonOptions);
        Assert.DoesNotContain(managerActive!.Data!.Items, x => x.EmployeeId == manager.EmployeeId);
        var employeeActive = await manager.EmployeeClient.GetFromJsonAsync<ApiResponse<PagedResult<AttendanceOperationalExceptionDto>>>("/api/attendance/me/exceptions?fromDate=2026-09-22&toDate=2026-09-22&exceptionType=EarlyDeparture&page=1&pageSize=10", JsonOptions);
        Assert.DoesNotContain(employeeActive!.Data!.Items, x => x.Id == dayId);
        var history = await manager.ManagerClient.GetFromJsonAsync<ApiResponse<PagedResult<AttendanceOperationsHistoryItem>>>($"/api/attendance/operations/employees/{manager.EmployeeId}/history?fromDate=2026-09-22&toDate=2026-09-22&page=1&pageSize=10", JsonOptions);
        Assert.Contains(history!.Data!.Items, x => x.AttendanceDayId == dayId);
    }

    [Theory]
    [InlineData(RoleNames.TimeManager)]
    [InlineData(RoleNames.HRBP)]
    public async Task Scoped_operational_role_resolution_updates_workbench_and_history(string roleName)
    {
        var builder = new AttendanceHttpEmployeeScenarioBuilder(factory);
        var graph = await builder.CreateManagerAsync("OPS-TM-SCOPE",
            employeePermissions: [Permissions.Attendance.ExceptionView],
            managerPermissions: [Permissions.Attendance.MonthlyViewTeam]);
        var outside = await builder.AddLinkedEmployeeAsync(graph, "Outside");
        var departmentA = Guid.NewGuid();
        var departmentB = Guid.NewGuid();
        var date = new DateOnly(2026, 9, 22);
        var dayA = Guid.NewGuid();
        var dayB = Guid.NewGuid();
        await factory.ExecuteInTenantScopeAsync(graph.Host, async services =>
        {
            var db = services.GetRequiredService<HRMS.Infrastructure.Persistence.HrmsDbContext>();
            var history = await db.EmployeeEmploymentHistory.Where(x => x.TenantId == graph.TenantId && (x.EmployeeId == graph.EmployeeId || x.EmployeeId == outside.EmployeeId)).ToListAsync();
            foreach (var item in history) item.DepartmentId = item.EmployeeId == graph.EmployeeId ? departmentA : departmentB;
            db.Departments.AddRange(new Department { Id = departmentA, TenantId = graph.TenantId, Code = $"TM-A-{departmentA:N}"[..12], Name = "Time Manager Scope A" }, new Department { Id = departmentB, TenantId = graph.TenantId, Code = $"TM-B-{departmentB:N}"[..12], Name = "Time Manager Scope B" });
            var roleId = HRMS.Infrastructure.Persistence.Seed.SeedData.RoleId(roleName);
            if (!await db.Roles.AnyAsync(x => x.Id == roleId)) db.Roles.Add(new Role { Id = roleId, Name = roleName, Description = $"{roleName} test scope" });
            var assignment = new UserRole { Id = Guid.NewGuid(), TenantId = graph.TenantId, UserId = graph.EmployeeUserId, RoleId = roleId, EffectiveFrom = new(2026, 1, 1), AssignmentSource = RoleAssignmentSource.System };
            assignment.Scopes.Add(new UserRoleAssignmentScope { Id = Guid.NewGuid(), TenantId = graph.TenantId, UserRoleAssignmentId = assignment.Id, ScopeType = RoleScopeType.Department, ScopeEntityId = departmentA });
            db.UserRoles.Add(assignment);
            db.AttendancePeriods.Add(new AttendancePeriod { Id = Guid.NewGuid(), TenantId = graph.TenantId, Year = 2026, Month = 9, StartDate = new(2026, 9, 1), EndDate = new(2026, 9, 30), DataVersion = 1 });
            db.EmployeeAttendanceDays.AddRange(
                new EmployeeAttendanceDay { Id = dayA, TenantId = graph.TenantId, EmployeeId = graph.EmployeeId, BusinessDate = date, Status = EmployeeAttendanceDayStatus.Present, IsLateIn = true, ScheduledStartUtc = date.ToDateTime(new(9, 0), DateTimeKind.Utc), FirstPunchAtUtc = date.ToDateTime(new(9, 20), DateTimeKind.Utc), ProcessedAtUtc = DateTime.UtcNow },
                new EmployeeAttendanceDay { Id = dayB, TenantId = graph.TenantId, EmployeeId = outside.EmployeeId, BusinessDate = date, Status = EmployeeAttendanceDayStatus.Present, IsLateIn = true, ScheduledStartUtc = date.ToDateTime(new(9, 0), DateTimeKind.Utc), FirstPunchAtUtc = date.ToDateTime(new(9, 20), DateTimeKind.Utc), ProcessedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        });

        var before = await graph.EmployeeClient.GetFromJsonAsync<ApiResponse<PagedResult<AttendanceOperationalExceptionDto>>>($"/api/attendance/operations/exceptions?fromDate={date:yyyy-MM-dd}&toDate={date:yyyy-MM-dd}&exceptionType=LateArrival&page=1&pageSize=10", JsonOptions);
        Assert.Contains(before!.Data!.Items, x => x.EmployeeId == graph.EmployeeId);
        Assert.DoesNotContain(before.Data.Items, x => x.EmployeeId == outside.EmployeeId);
        var resolved = await graph.EmployeeClient.PostAsJsonAsync("/api/attendance/operations/exceptions/resolve", new AttendanceExceptionResolutionRequest(dayA, AttendanceExceptionType.LateArrival, "Acknowledge", "Time Manager reviewed", 1), JsonOptions);
        Assert.Equal(HttpStatusCode.OK, resolved.StatusCode);
        var after = await graph.EmployeeClient.GetFromJsonAsync<ApiResponse<PagedResult<AttendanceOperationalExceptionDto>>>($"/api/attendance/operations/exceptions?fromDate={date:yyyy-MM-dd}&toDate={date:yyyy-MM-dd}&exceptionType=LateArrival&page=1&pageSize=10", JsonOptions);
        Assert.DoesNotContain(after!.Data!.Items, x => x.EmployeeId == graph.EmployeeId);
        Assert.DoesNotContain(after.Data.Items, x => x.EmployeeId == outside.EmployeeId);
        var outsideResolution = await graph.EmployeeClient.PostAsJsonAsync("/api/attendance/operations/exceptions/resolve", new AttendanceExceptionResolutionRequest(dayB, AttendanceExceptionType.LateArrival, "Acknowledge", "Out of scope attempt", 1), JsonOptions);
        Assert.Equal(HttpStatusCode.NotFound, outsideResolution.StatusCode);
        var inScopeHistory = await graph.EmployeeClient.GetFromJsonAsync<ApiResponse<PagedResult<AttendanceOperationsHistoryItem>>>($"/api/attendance/operations/employees/{graph.EmployeeId}/history?fromDate={date:yyyy-MM-dd}&toDate={date:yyyy-MM-dd}&page=1&pageSize=10", JsonOptions);
        Assert.Contains(inScopeHistory!.Data!.Items, x => x.AttendanceDayId == dayA && x.AttendanceVersion == 1);
        var outsideHistory = await graph.EmployeeClient.GetFromJsonAsync<ApiResponse<PagedResult<AttendanceOperationsHistoryItem>>>($"/api/attendance/operations/employees/{outside.EmployeeId}/history?fromDate={date:yyyy-MM-dd}&toDate={date:yyyy-MM-dd}&page=1&pageSize=10", JsonOptions);
        Assert.Empty(outsideHistory!.Data!.Items);
    }

    private async Task<Guid> SeedEarlyDayAsync(AttendanceHttpEmployeeScenario scenario, DateOnly date)
    {
        var dayId = Guid.NewGuid();
        await factory.ExecuteInTenantScopeAsync(scenario.Host, async services =>
        {
            var db = services.GetRequiredService<HRMS.Infrastructure.Persistence.HrmsDbContext>();
            db.AttendancePeriods.Add(new AttendancePeriod { Id = Guid.NewGuid(), TenantId = scenario.TenantId, Year = date.Year, Month = date.Month, StartDate = new(date.Year, date.Month, 1), EndDate = new(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month)), DataVersion = 1 });
            db.EmployeeAttendanceDays.Add(new EmployeeAttendanceDay { Id = dayId, TenantId = scenario.TenantId, EmployeeId = scenario.EmployeeId, BusinessDate = date, Status = EmployeeAttendanceDayStatus.Present, IsEarlyOut = true, ScheduledEndUtc = date.ToDateTime(new(18, 0), DateTimeKind.Utc), LastPunchAtUtc = date.ToDateTime(new(17, 30), DateTimeKind.Utc), ProcessedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        });
        return dayId;
    }

    private async Task<Guid> SeedLateDayAsync(AttendanceHttpEmployeeScenario scenario, DateOnly date)
    {
        var dayId = Guid.NewGuid();
        await factory.ExecuteInTenantScopeAsync(scenario.Host, async services =>
        {
            var db = services.GetRequiredService<HRMS.Infrastructure.Persistence.HrmsDbContext>();
            db.AttendancePeriods.Add(new AttendancePeriod { Id = Guid.NewGuid(), TenantId = scenario.TenantId, Year = date.Year, Month = date.Month, StartDate = new(date.Year, date.Month, 1), EndDate = new(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month)), DataVersion = 1 });
            db.EmployeeAttendanceDays.Add(new EmployeeAttendanceDay { Id = dayId, TenantId = scenario.TenantId, EmployeeId = scenario.EmployeeId, BusinessDate = date, Status = EmployeeAttendanceDayStatus.Present, IsLateIn = true, ScheduledStartUtc = date.ToDateTime(new(9, 0), DateTimeKind.Utc), FirstPunchAtUtc = date.ToDateTime(new(9, 15), DateTimeKind.Utc), ProcessedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        });
        return dayId;
    }

    private async Task SeedDayAsync(string host, Guid tenantId, Guid employeeId, DateOnly date)
    {
        await factory.ExecuteInTenantScopeAsync(host, async services =>
        {
            var db = services.GetRequiredService<HRMS.Infrastructure.Persistence.HrmsDbContext>();
            db.EmployeeAttendanceDays.Add(new EmployeeAttendanceDay
            {
                Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId,
                BusinessDate = date, Status = EmployeeAttendanceDayStatus.Absent,
                ExpectedWorkMinutes = 480, WorkedMinutes = 0,
                ProcessedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        });
    }

    private async Task<AttendanceHttpEmployeeScenario> CorrectionOperatorAsync(string prefix) =>
        await new AttendanceHttpEmployeeScenarioBuilder(factory).CreateAsync(prefix, [Permissions.Attendance.AdminCorrectionManage, Permissions.Attendance.ExceptionView, Permissions.Attendance.MonthlyViewAll]);

    private static AttendanceBulkCorrectionItem BulkItem(AttendanceHttpEmployeeScenario scenario, int version) =>
        new(scenario.EmployeeId, new(2026, 9, 22), AttendanceRegularizationType.CorrectInOutTime, new DateTime(2026, 9, 22, 9, 0, 0, DateTimeKind.Utc), new DateTime(2026, 9, 22, 18, 0, 0, DateTimeKind.Utc), "bounded correction", version);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
}

internal static class AttendanceHttpTestExtensions
{
    public static Task ExecuteTenantForTest(this string host, HrmsApiFactory factory, Guid tenantId, DateOnly start, DateOnly end, AttendancePeriodStatus? status) =>
        factory.ExecuteInTenantScopeAsync(host, async services =>
        {
            var db = services.GetRequiredService<HRMS.Infrastructure.Persistence.HrmsDbContext>();
            db.AttendancePeriods.Add(new AttendancePeriod { Id = Guid.NewGuid(), TenantId = tenantId, Year = start.Year, Month = start.Month, StartDate = start, EndDate = end, Status = status ?? AttendancePeriodStatus.Open, DataVersion = 1 });
            await db.SaveChangesAsync();
        });
}
