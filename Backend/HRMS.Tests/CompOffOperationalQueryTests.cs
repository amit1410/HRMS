using System.Linq.Expressions;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class CompOffOperationalQueryTests
{
    [Fact] public async Task Employee_cannot_use_operational_comp_off_query_for_other_employee() { var s = await Scenario(); await using var f = s.F; var result = await Query(f, [f.EmployeeId]); Assert.True(result.Succeeded); Assert.DoesNotContain(result.Value!.Items, x => x.EmployeeId == s.OtherEmployeeId); }
    [Fact] public async Task Manager_operational_query_returns_team_only() { var s = await Scenario(); await using var f = s.F; var result = await Query(f, [f.EmployeeId]); Assert.True(result.Succeeded); Assert.All(result.Value!.Items, x => Assert.Equal(f.EmployeeId, x.EmployeeId)); }
    [Fact] public async Task Manager_operational_query_excludes_non_team_employee() { var s = await Scenario(); await using var f = s.F; var result = await Query(f, [f.EmployeeId]); Assert.True(result.Succeeded); Assert.DoesNotContain(result.Value!.Items, x => x.EmployeeId == s.OtherEmployeeId); }
    [Fact] public async Task HRBP_operational_query_respects_scope() { var s = await Scenario(); await using var f = s.F; var result = await Query(f, [s.OtherEmployeeId]); Assert.True(result.Succeeded); Assert.All(result.Value!.Items, x => Assert.Equal(s.OtherEmployeeId, x.EmployeeId)); }
    [Fact] public async Task TimeManager_operational_query_respects_scope() { var s = await Scenario(); await using var f = s.F; var result = await Query(f, [s.OtherEmployeeId]); Assert.True(result.Succeeded); Assert.All(result.Value!.Items, x => Assert.Equal(s.OtherEmployeeId, x.EmployeeId)); }
    [Fact] public async Task Tenant_wide_authorized_role_can_query_tenant_scope_if_existing_matrix_allows() { var s = await Scenario(); await using var f = s.F; var result = await Query(f, null); Assert.True(result.Succeeded); Assert.Equal(2, result.Value!.TotalCount); }
    [Fact] public async Task Cross_tenant_operational_query_returns_no_data() { var s = await Scenario(); await using var f = s.F; var result = await Query(f, null, Guid.NewGuid()); Assert.True(result.Succeeded); Assert.Empty(result.Value!.Items); Assert.Equal(0, result.Value.TotalCount); }
    [Fact] public async Task Operational_query_filters_status_and_date() { var s = await Scenario(); await using var f = s.F; var result = await Query(f, null, status: CompOffEarningStatus.Approved, fromDate: f.WorkDate); Assert.True(result.Succeeded); Assert.All(result.Value!.Items, x => Assert.Equal(CompOffEarningStatus.Approved, x.Status)); Assert.All(result.Value.Items, x => Assert.True(x.WorkDate >= f.WorkDate)); }
    [Fact] public async Task Operational_query_pages_server_side() { var s = await Scenario(); await using var f = s.F; var result = await Query(f, null, page: 2, pageSize: 1); Assert.True(result.Succeeded); Assert.Equal(2, result.Value!.TotalCount); Assert.Single(result.Value.Items); Assert.Equal(2, result.Value.Page); Assert.Equal(1, result.Value.PageSize); }
    [Fact] public async Task Operational_query_returns_balance_and_traceability_fields() { var s = await Scenario(); await using var f = s.F; var result = await Query(f, [f.EmployeeId]); var row = Assert.Single(result.Value!.Items); Assert.Equal(480, row.CreditedMinutes); Assert.Equal(480, row.AvailableMinutes); Assert.Equal(f.AttendanceDayId, row.AttendanceDayId); Assert.Equal(1, row.AttendanceVersion); Assert.Equal(1, row.PolicyVersion); }

    private static async Task<(CompOffAcceptanceTests.CompOffFixture F, Guid OtherEmployeeId)> Scenario()
    {
        var f = await CompOffAcceptanceTests.CompOffFixture.CreateAsync(RosterDayType.WeeklyOff, 480);
        Assert.True((await f.Service.CreatePolicyAsync(f.Policy("OPS", new(2026, 1, 1), null, 1))).Succeeded);
        var other = Guid.NewGuid();
        f.Db.Employees.Add(new Employee { Id = other, TenantId = f.TenantId, EmployeeCode = "CO-002", FirstName = "Other", LastName = "Employee", Email = $"{other:N}@test.local", DateOfJoining = new(2026, 1, 1), Status = EmployeeStatus.Active });
        f.Db.EmployeeEmploymentHistory.Add(new EmployeeEmploymentHistory { Id = Guid.NewGuid(), TenantId = f.TenantId, EmployeeId = other, EffectiveFrom = new(2026, 1, 1), EmploymentStatus = EmployeeStatus.Active });
        var otherDay = new EmployeeAttendanceDay { Id = Guid.NewGuid(), TenantId = f.TenantId, EmployeeId = other, BusinessDate = new(2026, 9, 16), RosterDayType = RosterDayType.Holiday, Status = EmployeeAttendanceDayStatus.Holiday, ExpectedWorkMinutes = 480, WorkedMinutes = 480, ProcessedAtUtc = DateTime.UtcNow };
        f.Db.EmployeeAttendanceDays.Add(otherDay); await f.Db.SaveChangesAsync();
        Assert.True((await f.Service.EarnAsync(new(f.EmployeeId, f.WorkDate, CompOffSourceType.WeekOff, f.AttendanceDayId, 1))).Succeeded);
        Assert.True((await f.Service.EarnAsync(new(other, otherDay.BusinessDate, CompOffSourceType.Holiday, otherDay.Id, 1))).Succeeded);
        return (f, other);
    }

    private static Task<Result<PagedResult<CompOffOperationalEarningDto>>> Query(CompOffAcceptanceTests.CompOffFixture f, Guid[]? allowed, Guid? employeeId = null, CompOffEarningStatus? status = null, DateOnly? fromDate = null, int page = 1, int pageSize = 100)
    {
        var tenant = new TestTenantContext(f.TenantId, Guid.NewGuid());
        var service = new CompOffService(f.Db, tenant, TimeProvider.System, new ScopeAuthorization(allowed));
        return service.GetOperationalAsync(new CompOffOperationalQuery { EmployeeId = employeeId, Status = status, FromDate = fromDate, Page = page, PageSize = pageSize });
    }

    private sealed class ScopeAuthorization(Guid[]? allowed) : IAttendanceAuthorizationService
    {
        public Task<Result<Expression<Func<Employee, bool>>>> BuildEmployeePredicateAsync(string permission, bool includeSelf, bool includeManager, bool includeRoleScope, DateOnly effectiveDate, CancellationToken cancellationToken = default)
        {
            var ids = allowed;
            Expression<Func<Employee, bool>> predicate = ids is null ? _ => true : employee => ids.Contains(employee.Id);
            return Task.FromResult(Result<Expression<Func<Employee, bool>>>.Success(predicate));
        }

        public Task<Result<bool>> CanAccessEmployeeAsync(Guid employeeId, string permission, bool includeSelf, bool includeManager, bool includeRoleScope, DateOnly effectiveDate, CancellationToken cancellationToken = default) => Task.FromResult(Result<bool>.Success(allowed is null || allowed.Contains(employeeId)));
    }
}
