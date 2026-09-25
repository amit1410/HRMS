using System.Net.Http.Json;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Domain.Authorization;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HRMS.Tests;

public sealed class AttendanceOperationsDashboardTests : IClassFixture<HrmsApiFactory>
{
    private readonly HrmsApiFactory factory;

    public AttendanceOperationsDashboardTests(HrmsApiFactory factory) => this.factory = factory;

    [Fact]
    public async Task Dashboard_counts_match_authoritative_data()
    {
        await new AttendanceOperationsHttpTests(factory).Dashboard_exception_counts_match_operational_drilldown();
    }

    [Fact]
    public async Task Exception_count_matches_workbench_filter()
    {
        await new AttendanceOperationsHttpTests(factory).Dashboard_exception_counts_match_operational_drilldown();
    }

    [Fact]
    public async Task Pending_regularization_count_correct()
    {
        await new AttendanceOperationsHttpTests(factory).Dashboard_pending_corrections_drilldown_matches_scoped_date_filtered_queue();
    }

    [Fact]
    public async Task Pending_correction_count_correct()
    {
        await new AttendanceOperationsHttpTests(factory).Dashboard_pending_corrections_drilldown_matches_scoped_date_filtered_queue();
    }

    [Fact]
    public async Task Pending_od_count_correct()
    {
        var scenario = await New("OPS-DASH-OD");
        var date = new DateOnly(2026, 9, 22);
        await factory.ExecuteInTenantScopeAsync(scenario.Host, async services =>
        {
            var db = services.GetRequiredService<HRMS.Infrastructure.Persistence.HrmsDbContext>();
            db.AttendanceOnDutyRequests.Add(new AttendanceOnDutyRequest
            {
                Id = Guid.NewGuid(), TenantId = scenario.TenantId, EmployeeId = scenario.EmployeeId,
                StartDate = date, EndDate = date, Reason = "Dashboard pending OD",
                Status = AttendanceRequestStatus.Pending, SubmittedByUserId = scenario.UserId,
                SubmittedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        });
        var dashboard = await scenario.EmployeeClient.GetFromJsonAsync<ApiResponse<AttendanceOperationsDashboardDto>>(
            $"/api/attendance/operations/dashboard?fromDate={date:yyyy-MM-dd}&toDate={date:yyyy-MM-dd}");
        Assert.Equal(1, dashboard!.Data!.PendingOnDuty);
    }

    [Fact]
    public async Task Period_status_and_version_correct()
    {
        var scenario = await New("OPS-DASH-PERIOD");
        var date = new DateOnly(2026, 9, 22);
        await factory.ExecuteInTenantScopeAsync(scenario.Host, async services =>
        {
            var db = services.GetRequiredService<HRMS.Infrastructure.Persistence.HrmsDbContext>();
            db.AttendancePeriods.Add(new AttendancePeriod
            {
                Id = Guid.NewGuid(), TenantId = scenario.TenantId, Year = 2026, Month = 9,
                StartDate = new(2026, 9, 1), EndDate = new(2026, 9, 30), DataVersion = 3,
                Status = AttendancePeriodStatus.Closed
            });
            await db.SaveChangesAsync();
        });
        var result = await scenario.EmployeeClient.GetFromJsonAsync<ApiResponse<AttendanceOperationsDashboardDto>>(
            $"/api/attendance/operations/dashboard?fromDate={date:yyyy-MM-dd}&toDate={date:yyyy-MM-dd}");
        Assert.Equal(1, result!.Data!.FinalizedPeriods);
        Assert.Equal(0, result.Data.OpenPeriods);
    }

    [Fact]
    public async Task Tenant_scope_enforced()
    {
        var first = await New("OPS-DASH-TENANT-A");
        var second = await New("OPS-DASH-TENANT-B");
        await SeedDay(second.Host, second.TenantId, second.EmployeeId, EmployeeAttendanceDayStatus.Absent);
        var response = await first.EmployeeClient.GetFromJsonAsync<ApiResponse<AttendanceOperationsDashboardDto>>(
            "/api/attendance/operations/dashboard?fromDate=2026-09-22&toDate=2026-09-22");
        Assert.Equal(0, response!.Data!.Employees);
        Assert.Equal(0, response.Data.AbsentDays);
    }

    [Fact]
    public async Task Manager_scope_enforced_where_applicable()
    {
        var builder = new AttendanceHttpEmployeeScenarioBuilder(factory);
        var manager = await builder.CreateManagerAsync("OPS-DASH-MANAGER",
            managerPermissions: [Permissions.Attendance.ExceptionView, Permissions.Attendance.MonthlyViewTeam]);
        var team = await builder.AddLinkedEmployeeAsync(manager, "TeamAttendance");
        var outsideId = Guid.NewGuid();
        await factory.ExecuteInTenantScopeAsync(manager.Host, async services =>
        {
            var db = services.GetRequiredService<HRMS.Infrastructure.Persistence.HrmsDbContext>();
            db.Employees.Add(new Employee { Id = outsideId, TenantId = manager.TenantId, EmployeeCode = "DASH-OUT", FirstName = "Outside", LastName = "Scope", DateOfJoining = new(2026, 1, 1) });
            db.EmployeeEmploymentHistory.Add(new EmployeeEmploymentHistory { Id = Guid.NewGuid(), TenantId = manager.TenantId, EmployeeId = outsideId, EffectiveFrom = new(2026, 1, 1), EmploymentStatus = EmployeeStatus.Active, CreatedBy = "dashboard-test" });
            db.EmployeeAttendanceDays.AddRange(
                new EmployeeAttendanceDay { Id = Guid.NewGuid(), TenantId = manager.TenantId, EmployeeId = team.EmployeeId, BusinessDate = new(2026, 9, 22), Status = EmployeeAttendanceDayStatus.Present, ProcessedAtUtc = DateTime.UtcNow },
                new EmployeeAttendanceDay { Id = Guid.NewGuid(), TenantId = manager.TenantId, EmployeeId = outsideId, BusinessDate = new(2026, 9, 22), Status = EmployeeAttendanceDayStatus.Present, ProcessedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        });
        var result = await manager.ManagerClient.GetFromJsonAsync<ApiResponse<AttendanceOperationsDashboardDto>>(
            "/api/attendance/operations/dashboard?fromDate=2026-09-22&toDate=2026-09-22");
        Assert.Equal(1, result!.Data!.Employees);
        Assert.Equal(1, result.Data.ProcessedDays);
    }

    private Task<AttendanceHttpEmployeeScenario> New(string prefix) =>
        new AttendanceHttpEmployeeScenarioBuilder(factory).CreateAsync(prefix,
            [Permissions.Attendance.ExceptionView, Permissions.Attendance.RegularizationApprove, Permissions.Attendance.MonthlyViewAll]);

    private Task SeedDay(string host, Guid tenantId, Guid employeeId, EmployeeAttendanceDayStatus status) =>
        factory.ExecuteInTenantScopeAsync(host, async services =>
        {
            var db = services.GetRequiredService<HRMS.Infrastructure.Persistence.HrmsDbContext>();
            db.EmployeeAttendanceDays.Add(new EmployeeAttendanceDay
            {
                Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId,
                BusinessDate = new(2026, 9, 22), Status = status, ProcessedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        });
}
