using System.Net;
using HRMS.Domain.Authorization;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HRMS.Tests;

public sealed class AttendanceOperationsExportTests : IClassFixture<HrmsApiFactory>
{
    private readonly HrmsApiFactory factory;

    public AttendanceOperationsExportTests(HrmsApiFactory factory) => this.factory = factory;

    [Fact]
    public async Task Exception_export_matches_filter()
    {
        await new AttendanceOperationsHttpTests(factory).Operational_exception_export_is_filtered_and_reads_bounded_workbench_pages();
    }

    [Fact]
    public async Task Export_respects_tenant()
    {
        await new AttendanceOperationsHttpTests(factory).Operational_exception_export_respects_tenant_scope();
    }

    [Fact]
    public async Task Export_respects_manager_scope()
    {
        var builder = new AttendanceHttpEmployeeScenarioBuilder(factory);
        var manager = await builder.CreateManagerAsync("OPS-EXPORT-MANAGER",
            managerPermissions: [Permissions.Attendance.ExceptionView, Permissions.Attendance.ReportExport, Permissions.Attendance.MonthlyViewTeam]);
        var team = await builder.AddLinkedEmployeeAsync(manager, "InScope");
        var outsideId = Guid.NewGuid();
        await factory.ExecuteInTenantScopeAsync(manager.Host, async services =>
        {
            var db = services.GetRequiredService<HRMS.Infrastructure.Persistence.HrmsDbContext>();
            db.Employees.Add(new Employee { Id = outsideId, TenantId = manager.TenantId, EmployeeCode = "EXPORT-OUT", FirstName = "Outside", LastName = "Scope", DateOfJoining = new(2026, 1, 1) });
            db.EmployeeEmploymentHistory.Add(new EmployeeEmploymentHistory { Id = Guid.NewGuid(), TenantId = manager.TenantId, EmployeeId = outsideId, EffectiveFrom = new(2026, 1, 1), EmploymentStatus = EmployeeStatus.Active, CreatedBy = "export-test" });
            AddAbsentDay(db, manager.TenantId, team.EmployeeId);
            AddAbsentDay(db, manager.TenantId, outsideId);
            await db.SaveChangesAsync();
        });
        var response = await manager.ManagerClient.GetAsync("/api/attendance/operations/exceptions/export?fromDate=2026-09-22&toDate=2026-09-22");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var csv = await response.Content.ReadAsStringAsync();
        Assert.Contains(team.EmployeeCode, csv);
        Assert.DoesNotContain("EXPORT-OUT", csv);
    }

    [Fact]
    public async Task Export_respects_hrbp_scope()
    {
        await AssertRoleScope(RoleNames.HRBP, "OPS-EXPORT-HRBP");
    }

    [Fact]
    public async Task Export_respects_time_manager_scope()
    {
        await AssertRoleScope(RoleNames.TimeManager, "OPS-EXPORT-TM");
    }

    [Fact]
    public async Task Export_does_not_include_unrequested_sensitive_fields()
    {
        var scenario = await new AttendanceHttpEmployeeScenarioBuilder(factory).CreateAsync("OPS-EXPORT-SENSITIVE",
            [Permissions.Attendance.ExceptionView, Permissions.Attendance.ReportExport, Permissions.Attendance.MonthlyViewAll]);
        await factory.ExecuteInTenantScopeAsync(scenario.Host, async services =>
        {
            var db = services.GetRequiredService<HRMS.Infrastructure.Persistence.HrmsDbContext>();
            AddAbsentDay(db, scenario.TenantId, scenario.EmployeeId);
            await db.SaveChangesAsync();
        });
        var response = await scenario.EmployeeClient.GetAsync("/api/attendance/operations/exceptions/export?fromDate=2026-09-22&toDate=2026-09-22");
        var csv = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(scenario.EmployeeId.ToString(), csv);
        Assert.DoesNotContain("@test", csv, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Large_export_is_bounded_or_streamed()
    {
        // The endpoint reads canonical workbench pages of PagedQuery.MaxPageSize and rejects over 50,000 rows.
        await new AttendanceOperationsHttpTests(factory).Operational_exception_export_is_filtered_and_reads_bounded_workbench_pages();
    }

    [Fact]
    public async Task Export_is_read_only()
    {
        var scenario = await new AttendanceHttpEmployeeScenarioBuilder(factory).CreateAsync("OPS-EXPORT-READONLY",
            [Permissions.Attendance.ExceptionView, Permissions.Attendance.ReportExport, Permissions.Attendance.MonthlyViewAll]);
        await factory.ExecuteInTenantScopeAsync(scenario.Host, async services =>
        {
            var db = services.GetRequiredService<HRMS.Infrastructure.Persistence.HrmsDbContext>();
            AddAbsentDay(db, scenario.TenantId, scenario.EmployeeId);
            await db.SaveChangesAsync();
        });
        var before = await CountDays(scenario);
        var response = await scenario.EmployeeClient.GetAsync("/api/attendance/operations/exceptions/export?fromDate=2026-09-22&toDate=2026-09-22");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(before, await CountDays(scenario));
    }

    private async Task AssertRoleScope(string roleName, string prefix)
    {
        var builder = new AttendanceHttpEmployeeScenarioBuilder(factory);
        var graph = await builder.CreateManagerAsync(prefix,
            employeePermissions: [Permissions.Attendance.ExceptionView, Permissions.Attendance.ReportExport]);
        var outside = await builder.AddLinkedEmployeeAsync(graph, "OutOfScope");
        var deptIn = Guid.NewGuid();
        var deptOut = Guid.NewGuid();
        string? inScopeCode = null;
        await factory.ExecuteInTenantScopeAsync(graph.Host, async services =>
        {
            var db = services.GetRequiredService<HRMS.Infrastructure.Persistence.HrmsDbContext>();
            inScopeCode = await db.Employees.Where(x => x.TenantId == graph.TenantId && x.Id == graph.EmployeeId).Select(x => x.EmployeeCode).SingleAsync();
            db.Departments.AddRange(
                new Department { Id = deptIn, TenantId = graph.TenantId, Code = $"EI-{deptIn:N}"[..12], Name = "Export In Scope" },
                new Department { Id = deptOut, TenantId = graph.TenantId, Code = $"EO-{deptOut:N}"[..12], Name = "Export Out Scope" });
            var employment = await db.EmployeeEmploymentHistory.Where(x => x.TenantId == graph.TenantId && (x.EmployeeId == graph.EmployeeId || x.EmployeeId == outside.EmployeeId)).ToListAsync();
            foreach (var item in employment) item.DepartmentId = item.EmployeeId == graph.EmployeeId ? deptIn : deptOut;
            var roleId = SeedData.RoleId(roleName);
            if (!await db.Roles.AnyAsync(x => x.Id == roleId)) db.Roles.Add(new Role { Id = roleId, Name = roleName, Description = "Attendance export scope test" });
            var assignment = new UserRole { Id = Guid.NewGuid(), TenantId = graph.TenantId, UserId = graph.EmployeeUserId, RoleId = roleId, EffectiveFrom = new(2026, 1, 1), AssignmentSource = RoleAssignmentSource.System };
            assignment.Scopes.Add(new UserRoleAssignmentScope { Id = Guid.NewGuid(), TenantId = graph.TenantId, UserRoleAssignmentId = assignment.Id, ScopeType = RoleScopeType.Department, ScopeEntityId = deptIn });
            db.UserRoles.Add(assignment);
            AddAbsentDay(db, graph.TenantId, graph.EmployeeId);
            AddAbsentDay(db, graph.TenantId, outside.EmployeeId);
            await db.SaveChangesAsync();
        });
        var response = await graph.EmployeeClient.GetAsync("/api/attendance/operations/exceptions/export?fromDate=2026-09-22&toDate=2026-09-22");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var csv = await response.Content.ReadAsStringAsync();
        Assert.Contains(inScopeCode!, csv);
        Assert.DoesNotContain(outside.EmployeeCode, csv);
    }

    private static void AddAbsentDay(HRMS.Infrastructure.Persistence.HrmsDbContext db, Guid tenantId, Guid employeeId) =>
        db.EmployeeAttendanceDays.Add(new EmployeeAttendanceDay
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId,
            BusinessDate = new(2026, 9, 22), Status = EmployeeAttendanceDayStatus.Absent,
            ProcessedAtUtc = DateTime.UtcNow
        });

    private async Task<int> CountDays(AttendanceHttpEmployeeScenario scenario)
    {
        var count = 0;
        await factory.ExecuteInTenantScopeAsync(scenario.Host, async services =>
            count = await services.GetRequiredService<HRMS.Infrastructure.Persistence.HrmsDbContext>().EmployeeAttendanceDays
                .CountAsync(x => x.TenantId == scenario.TenantId && x.EmployeeId == scenario.EmployeeId));
        return count;
    }
}
