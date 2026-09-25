using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Domain.Entities;
using HRMS.Domain.Authorization;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HRMS.Tests;

/// <summary>Security acceptance through the API pipeline, authorization policies and scoped queries.</summary>
public sealed class AttendanceOperationsSecurityTests : IClassFixture<HrmsApiFactory>
{
    private readonly HrmsApiFactory factory;

    public AttendanceOperationsSecurityTests(HrmsApiFactory factory) => this.factory = factory;

    [Fact]
    public async Task Employee_can_view_own_exceptions()
    {
        await new AttendanceOperationsHttpTests(factory).Employee_can_view_own_exceptions_but_not_operational_scope();
    }

    [Fact]
    public async Task Employee_cannot_view_other_employee_exceptions()
    {
        var builder = new AttendanceHttpEmployeeScenarioBuilder(factory);
        var manager = await builder.CreateManagerAsync("OPS-SEC-OTHER", employeePermissions: [Permissions.Attendance.View]);
        var other = await builder.AddLinkedEmployeeAsync(manager, "Other");
        await factory.ExecuteInTenantScopeAsync(manager.Host, async services =>
        {
            var db = services.GetRequiredService<HRMS.Infrastructure.Persistence.HrmsDbContext>();
            db.EmployeeAttendanceDays.Add(new EmployeeAttendanceDay
            {
                Id = Guid.NewGuid(), TenantId = manager.TenantId, EmployeeId = other.EmployeeId,
                BusinessDate = new DateOnly(2026, 9, 22), Status = EmployeeAttendanceDayStatus.Absent,
                ProcessedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        });
        var response = await manager.EmployeeClient.GetAsync(
            $"/api/attendance/operations/exceptions?employeeId={other.EmployeeId}&fromDate=2026-09-22&toDate=2026-09-22");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Employee_cannot_approve_own_manual_attendance()
    {
        var builder = new AttendanceHttpEmployeeScenarioBuilder(factory);
        var manager = await builder.CreateManagerAsync("OPS-SEC-SELF-APPROVE",
            managerPermissions: [Permissions.Attendance.AdminCorrectionManage, Permissions.Attendance.RegularizationApprove]);
        var team = await builder.AddLinkedEmployeeAsync(manager, "MakerTarget");
        var date = new DateOnly(2026, 9, 22);
        await factory.ExecuteInTenantScopeAsync(manager.Host, async services =>
        {
            var db = services.GetRequiredService<HRMS.Infrastructure.Persistence.HrmsDbContext>();
            db.AttendancePeriods.Add(new AttendancePeriod { Id = Guid.NewGuid(), TenantId = manager.TenantId, Year = 2026, Month = 9, StartDate = new(2026, 9, 1), EndDate = new(2026, 9, 30), DataVersion = 1 });
            db.EmployeeAttendanceDays.Add(new EmployeeAttendanceDay { Id = Guid.NewGuid(), TenantId = manager.TenantId, EmployeeId = team.EmployeeId, BusinessDate = date, Status = EmployeeAttendanceDayStatus.Absent, ProcessedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        });
        var request = await manager.ManagerClient.PostAsJsonAsync("/api/attendance/operations/manual",
            new ManualAttendanceRequest(team.EmployeeId, date, AttendanceRegularizationType.CorrectInOutTime,
                date.ToDateTime(new TimeOnly(9, 0), DateTimeKind.Utc), date.ToDateTime(new TimeOnly(18, 0), DateTimeKind.Utc), "self approval security", null, 1));
        Assert.Equal(HttpStatusCode.OK, request.StatusCode);
        using var body = JsonDocument.Parse(await request.Content.ReadAsStringAsync());
        var requestId = body.RootElement.GetProperty("data").GetProperty("id").GetGuid();
        var response = await manager.ManagerClient.PostAsync(
            $"/api/attendance/manager/regularizations/{requestId}/approve", null);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await factory.ExecuteInTenantScopeAsync(manager.Host, async services =>
        {
            var db = services.GetRequiredService<HRMS.Infrastructure.Persistence.HrmsDbContext>();
            Assert.Equal(AttendanceRequestStatus.Pending,
                await db.AttendanceRegularizationRequests.Where(x => x.Id == requestId).Select(x => x.Status).SingleAsync());
            Assert.Empty(await db.AttendanceAdjustments.Where(x => x.AttendanceRegularizationRequestId == requestId).ToListAsync());
        });
    }

    [Fact]
    public async Task Manager_team_scope_enforced()
    {
        await new AttendanceOperationsHttpTests(factory).Manager_operational_query_excludes_same_tenant_non_team_employee();
    }

    [Fact]
    public async Task HRBP_scope_enforced()
    {
        await new AttendanceOperationsHttpTests(factory).Scoped_operational_role_resolution_updates_workbench_and_history(RoleNames.HRBP);
    }

    [Fact]
    public async Task TimeManager_scope_enforced()
    {
        await new AttendanceOperationsHttpTests(factory).Scoped_operational_role_resolution_updates_workbench_and_history(RoleNames.TimeManager);
    }

    [Fact]
    public async Task Cross_tenant_exception_access_denied()
    {
        await new AttendanceOperationsHttpTests(factory).Cross_tenant_operational_query_returns_no_data();
    }

    [Fact]
    public async Task Unauthorized_user_cannot_bulk_correct()
    {
        var employee = await new AttendanceHttpEmployeeScenarioBuilder(factory).CreateAsync("OPS-SEC-BULK");
        var response = await employee.EmployeeClient.PostAsJsonAsync(
            "/api/attendance/operations/bulk-corrections", Array.Empty<object>());
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Unauthorized_user_cannot_reopen_period()
    {
        var employee = await new AttendanceHttpEmployeeScenarioBuilder(factory).CreateAsync("OPS-SEC-REOPEN");
        var response = await employee.EmployeeClient.PostAsJsonAsync(
            $"/api/attendance/periods/{Guid.NewGuid()}/reopen", new { reason = "not authorized" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Export_respects_authorization_scope()
    {
        var employee = await new AttendanceHttpEmployeeScenarioBuilder(factory).CreateAsync(
            "OPS-SEC-EXPORT", [Permissions.Attendance.ExceptionView]);
        var response = await employee.EmployeeClient.GetAsync("/api/attendance/operations/exceptions/export");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Locked_period_cannot_be_modified_without_reopen_permission()
    {
        var manager = await new AttendanceHttpEmployeeScenarioBuilder(factory).CreateManagerAsync(
            "OPS-SEC-LOCK", managerPermissions: [Permissions.Attendance.AdminCorrectionManage]);
        var team = await new AttendanceHttpEmployeeScenarioBuilder(factory).AddLinkedEmployeeAsync(manager, "LockedTarget");
        var date = new DateOnly(2026, 9, 22);
        await factory.ExecuteInTenantScopeAsync(manager.Host, async services =>
        {
            var db = services.GetRequiredService<HRMS.Infrastructure.Persistence.HrmsDbContext>();
            db.AttendancePeriods.Add(new AttendancePeriod { Id = Guid.NewGuid(), TenantId = manager.TenantId, Year = 2026, Month = 9, StartDate = new(2026, 9, 1), EndDate = new(2026, 9, 30), DataVersion = 1, Status = AttendancePeriodStatus.Closed });
            db.EmployeeAttendanceDays.Add(new EmployeeAttendanceDay { Id = Guid.NewGuid(), TenantId = manager.TenantId, EmployeeId = team.EmployeeId, BusinessDate = date, Status = EmployeeAttendanceDayStatus.Absent, ProcessedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        });
        var response = await manager.ManagerClient.PostAsJsonAsync(
            "/api/attendance/operations/manual", new ManualAttendanceRequest(team.EmployeeId, date,
                AttendanceRegularizationType.CorrectInOutTime, date.ToDateTime(new TimeOnly(9, 0), DateTimeKind.Utc),
                date.ToDateTime(new TimeOnly(18, 0), DateTimeKind.Utc), "locked period security", null, 1));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Cross_employee_correction_reference_denied()
    {
        await new AttendanceOperationsHttpTests(factory).Bulk_attendance_correction_respects_employee_scope();
    }
}
