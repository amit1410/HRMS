using HRMS.Application.Common;
using HRMS.Application.DTOs.Employees;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class EmployeeSupervisorSearchTests
{
    private static readonly Guid Tenant = SeedData.TenantIds.Demo01;
    private static readonly Guid Employee = OrganizationTestHarness.EmployeeId(Tenant, "EMP-001");
    private static readonly DateOnly Today = new(2026, 3, 4);

    [Fact]
    public async Task Search_returns_active_employees_without_manager_categories_and_uses_stable_paging()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        var ids = await AddCandidatesAsync(harness, new[]
        {
            Candidate("SUP-001", "Name Match"),
            Candidate("SUP-002", "Department Match"),
            Candidate("SUP-003", "Location Match")
        });

        var first = await harness.Supervisors().GetSupervisorOptionsAsync(Employee, "L1",
            new TestPagedQuery { Search = "SUP-", Page = 1, PageSize = 2 });
        var second = await harness.Supervisors().GetSupervisorOptionsAsync(Employee, "L1",
            new TestPagedQuery { Search = "SUP-", Page = 2, PageSize = 2 });

        Assert.True(first.Succeeded, first.Message);
        Assert.True(second.Succeeded, second.Message);
        var returned = first.Value!.Items.Concat(second.Value!.Items).Select(x => x.EmployeeId).ToArray();
        Assert.Equal(ids.OrderBy(x => x).ToArray(), returned.OrderBy(x => x).ToArray());
        Assert.Equal(returned.Length, returned.Distinct().Count());
    }

    [Fact]
    public async Task Search_requires_employee_status_active_even_when_history_is_active()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        var inactive = Candidate("SUP-INACTIVE", "Inactive");
        inactive.Status = EmployeeStatus.Resigned;
        var ineligibleHistory = Candidate("SUP-INELIGIBLE", "Ineligible");
        await AddCandidatesAsync(harness, [inactive, ineligibleHistory]);

        using (var context = harness.CreateContext())
        {
            var history = await context.EmployeeEmploymentHistory
                .SingleAsync(x => x.EmployeeId == ineligibleHistory.Id);
            history.EmploymentStatus = EmployeeStatus.Resigned;
            await context.SaveChangesAsync();
        }

        var result = await harness.Supervisors().GetSupervisorOptionsAsync(Employee, "L1",
            new TestPagedQuery { Search = "SUP-", PageSize = 100 });

        Assert.True(result.Succeeded, result.Message);
        Assert.DoesNotContain(result.Value!.Items, x => x.EmployeeId == inactive.Id);
        Assert.DoesNotContain(result.Value.Items, x => x.EmployeeId == ineligibleHistory.Id);
    }

    [Fact]
    public async Task Assignment_validation_matches_search_for_self_inactive_and_cross_tenant_references()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        var inactive = Candidate("SUP-INACTIVE", "Inactive");
        inactive.Status = EmployeeStatus.Resigned;
        await AddCandidatesAsync(harness, [inactive]);

        var self = await harness.Supervisors().UpsertAsync(Employee, new EmployeeSupervisorRequest { L1ManagerId = Employee });
        var inactiveResult = await harness.Supervisors().UpsertAsync(Employee, new EmployeeSupervisorRequest { L1ManagerId = inactive.Id });
        var crossTenant = await harness.Supervisors().UpsertAsync(Employee, new EmployeeSupervisorRequest
        {
            L1ManagerId = OrganizationTestHarness.EmployeeId(SeedData.TenantIds.Demo02, "E-100")
        });

        Assert.Equal("l1ManagerId", self.Errors!.Single().Field);
        Assert.Equal("l1ManagerId", inactiveResult.Errors!.Single().Field);
        Assert.Equal("l1ManagerId", crossTenant.Errors!.Single().Field);
    }

    [Fact]
    public async Task Every_frontend_supervisor_role_uses_the_same_eligible_population()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        var candidate = Candidate("SUP-ALL", "All Roles");
        await AddCandidatesAsync(harness, [candidate]);

        foreach (var role in new[] { "L1", "L2", "L3", "Other", "HR", "Time" })
        {
            var result = await harness.Supervisors().GetSupervisorOptionsAsync(Employee, role,
                new TestPagedQuery { Search = "SUP-ALL", PageSize = 100 });
            Assert.True(result.Succeeded, $"{role}: {result.Message}");
            Assert.Contains(result.Value!.Items, x => x.EmployeeId == candidate.Id);
        }
    }

    private static Employee Candidate(string code, string name) => new()
    {
        Id = Guid.NewGuid(), TenantId = Tenant, EmployeeCode = code,
        FirstName = name, LastName = "Employee", Email = $"{code.ToLowerInvariant()}@test.local",
        DateOfJoining = Today, Status = EmployeeStatus.Active, ManagerCategories = SupervisorType.None
    };

    private static async Task<Guid[]> AddCandidatesAsync(OrganizationTestHarness harness, IEnumerable<Employee> employees)
    {
        using var context = harness.CreateContext();
        var list = employees.ToArray();
        context.Employees.AddRange(list);
        context.EmployeeEmploymentHistory.AddRange(list.Select(employee => new EmployeeEmploymentHistory
        {
            Id = Guid.NewGuid(), TenantId = Tenant, EmployeeId = employee.Id,
            EffectiveFrom = Today, EmploymentStatus = EmployeeStatus.Active
        }));
        await context.SaveChangesAsync();
        return list.Select(x => x.Id).ToArray();
    }

    private sealed class TestPagedQuery : PagedQuery { }
}
