using HRMS.Application.Common;
using HRMS.Application.DTOs.Departments;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

/// <summary>
/// Department behaviour, exercised through the real service over a real (SQLite) database so the tenant
/// query filter and the unique indexes participate rather than being mocked away.
/// </summary>
public class DepartmentServiceTests
{
    private static readonly Guid Demo01 = SeedData.TenantIds.Demo01;
    private static readonly Guid Demo02 = SeedData.TenantIds.Demo02;

    [Fact]
    public async Task Get_returns_only_the_callers_own_departments()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var mine = await harness.ActAs(Demo01).Departments().GetAsync(new DepartmentQuery());
        var theirs = await harness.ActAs(Demo02).Departments().GetAsync(new DepartmentQuery());

        Assert.Equal(new[] { "ENG", "FIN", "HR" }, mine.Value!.Items.Select(d => d.Code));
        Assert.Equal(new[] { "OPS", "SLS" }, theirs.Value!.Items.Select(d => d.Code));
    }

    [Fact]
    public async Task Get_filters_by_active_state()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var finance = OrganizationTestHarness.DepartmentId(Demo01, "FIN");
        await harness.Departments().UpdateAsync(finance, Request("FIN", "Finance", isActive: false));

        var active = await harness.Departments().GetAsync(new DepartmentQuery { IsActive = true });
        var retired = await harness.Departments().GetAsync(new DepartmentQuery { IsActive = false });

        Assert.Equal(new[] { "ENG", "HR" }, active.Value!.Items.Select(d => d.Code));
        Assert.Equal(new[] { "FIN" }, retired.Value!.Items.Select(d => d.Code));
    }

    [Fact]
    public async Task Search_matches_code_and_name_regardless_of_case()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var byCode = await harness.Departments().GetAsync(new DepartmentQuery { Search = "eng" });
        var byName = await harness.Departments().GetAsync(new DepartmentQuery { Search = "HUMAN" });
        var noMatch = await harness.Departments().GetAsync(new DepartmentQuery { Search = "logistics" });

        Assert.Equal(new[] { "ENG" }, byCode.Value!.Items.Select(d => d.Code));
        Assert.Equal(new[] { "HR" }, byName.Value!.Items.Select(d => d.Code));
        Assert.Empty(noMatch.Value!.Items);
    }

    /// <summary>
    /// The employee count is an aggregate over a tenant-filtered navigation, so it must count this tenant's
    /// people only — a count that leaked across tenants would disclose the size of another organization.
    /// </summary>
    [Fact]
    public async Task Employee_count_covers_only_the_callers_tenant()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var departments = (await harness.Departments().GetAsync(new DepartmentQuery())).Value!.Items;

        Assert.Equal(4, departments.Single(d => d.Code == "ENG").EmployeeCount);
        Assert.Equal(1, departments.Single(d => d.Code == "HR").EmployeeCount);

        var otherTenant = (await harness.ActAs(Demo02).Departments().GetAsync(new DepartmentQuery())).Value!.Items;
        Assert.Equal(1, otherTenant.Single(d => d.Code == "OPS").EmployeeCount);
    }

    [Fact]
    public async Task GetById_of_another_tenants_department_is_not_found()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var theirs = OrganizationTestHarness.DepartmentId(Demo02, "OPS");
        var result = await harness.ActAs(Demo01).Departments().GetByIdAsync(theirs);

        Assert.Equal(ResultStatus.NotFound, result.Status);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task Create_stamps_the_authenticated_tenant()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var result = await harness.Departments().CreateAsync(Request("QA", "Quality Assurance"));

        Assert.True(result.Succeeded);
        Assert.Equal(0, result.Value!.EmployeeCount);

        using var unscoped = harness.CreateUnscopedContext();
        var saved = await unscoped.Departments.IgnoreQueryFilters().SingleAsync(d => d.Id == result.Value.Id);
        Assert.Equal(Demo01, saved.TenantId);
    }

    [Fact]
    public async Task Create_trims_input_and_treats_a_blank_description_as_absent()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var result = await harness.Departments().CreateAsync(Request("  QA  ", "  Quality Assurance  ", "   "));

        Assert.Equal("QA", result.Value!.Code);
        Assert.Equal("Quality Assurance", result.Value.Name);
        Assert.Null(result.Value.Description);
    }

    [Fact]
    public async Task Create_rejects_a_duplicate_code_regardless_of_case()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var result = await harness.Departments().CreateAsync(Request("eng", "Engineering Platform"));

        Assert.Equal(ResultStatus.Conflict, result.Status);
        Assert.Equal("code", Assert.Single(result.Errors!).Field);
    }

    [Fact]
    public async Task Create_rejects_a_duplicate_name()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var result = await harness.Departments().CreateAsync(Request("ENG2", "engineering"));

        Assert.Equal(ResultStatus.Conflict, result.Status);
        Assert.Equal("name", Assert.Single(result.Errors!).Field);
    }

    /// <summary>Codes are unique per tenant, not globally: two organizations may both have an "ENG".</summary>
    [Fact]
    public async Task Another_tenant_may_reuse_a_code_that_is_taken_here()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var result = await harness.ActAs(Demo02).Departments().CreateAsync(Request("ENG", "Engineering"));

        Assert.True(result.Succeeded);

        using var unscoped = harness.CreateUnscopedContext();
        var withCode = await unscoped.Departments.IgnoreQueryFilters()
            .Where(d => d.Code == "ENG")
            .Select(d => d.TenantId)
            .ToListAsync();
        Assert.Equal(2, withCode.Count);
        Assert.Contains(Demo01, withCode);
        Assert.Contains(Demo02, withCode);
    }

    [Fact]
    public async Task Update_replaces_the_record_and_reports_the_employee_count()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var engineering = OrganizationTestHarness.DepartmentId(Demo01, "ENG");
        var result = await harness.Departments()
            .UpdateAsync(engineering, Request("ENG", "Engineering", "Now includes platform.", isActive: false));

        Assert.True(result.Succeeded);
        Assert.Equal("Now includes platform.", result.Value!.Description);
        Assert.False(result.Value.IsActive);
        Assert.Equal(4, result.Value.EmployeeCount);
        Assert.NotNull(result.Value.ModifiedDate);
    }

    [Fact]
    public async Task Update_of_another_tenants_department_is_not_found_and_changes_nothing()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var theirs = OrganizationTestHarness.DepartmentId(Demo02, "OPS");
        var result = await harness.ActAs(Demo01).Departments()
            .UpdateAsync(theirs, Request("OPS", "Hijacked"));

        Assert.Equal(ResultStatus.NotFound, result.Status);

        using var unscoped = harness.CreateUnscopedContext();
        var untouched = await unscoped.Departments.IgnoreQueryFilters().SingleAsync(d => d.Id == theirs);
        Assert.Equal("Operations", untouched.Name);
        Assert.Null(untouched.ModifiedDate);
    }

    [Fact]
    public async Task Update_lets_a_department_keep_its_own_code_and_name()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var hr = OrganizationTestHarness.DepartmentId(Demo01, "HR");
        var result = await harness.Departments()
            .UpdateAsync(hr, Request("HR", "Human Resources", "People team."));

        Assert.True(result.Succeeded);
        Assert.Equal("People team.", result.Value!.Description);
    }

    [Fact]
    public async Task Update_rejects_a_code_another_department_already_uses()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var hr = OrganizationTestHarness.DepartmentId(Demo01, "HR");
        var result = await harness.Departments().UpdateAsync(hr, Request("FIN", "People"));

        Assert.Equal(ResultStatus.Conflict, result.Status);
        Assert.Equal("code", Assert.Single(result.Errors!).Field);
    }

    /// <summary>
    /// A department with people in it is retired, not deleted — deleting it would strip the unit from those
    /// employees' history. The conflict message says how many are in the way.
    /// </summary>
    [Fact]
    public async Task Delete_refuses_a_department_that_still_has_employees()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var engineering = OrganizationTestHarness.DepartmentId(Demo01, "ENG");
        var result = await harness.Departments().DeleteAsync(engineering);

        Assert.Equal(ResultStatus.Conflict, result.Status);
        Assert.Contains("4 employee(s)", result.Message);

        using var unscoped = harness.CreateUnscopedContext();
        Assert.True(await unscoped.Departments.IgnoreQueryFilters().AnyAsync(d => d.Id == engineering));
    }

    [Fact]
    public async Task Delete_removes_a_department_nobody_is_assigned_to()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var created = (await harness.Departments().CreateAsync(Request("QA", "Quality Assurance"))).Value!;
        var result = await harness.Departments().DeleteAsync(created.Id);

        Assert.True(result.Succeeded);

        using var unscoped = harness.CreateUnscopedContext();
        Assert.False(await unscoped.Departments.IgnoreQueryFilters().AnyAsync(d => d.Id == created.Id));
    }

    [Fact]
    public async Task Delete_of_another_tenants_department_is_not_found()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var theirs = OrganizationTestHarness.DepartmentId(Demo02, "SLS");
        var result = await harness.ActAs(Demo01).Departments().DeleteAsync(theirs);

        Assert.Equal(ResultStatus.NotFound, result.Status);

        using var unscoped = harness.CreateUnscopedContext();
        Assert.True(await unscoped.Departments.IgnoreQueryFilters().AnyAsync(d => d.Id == theirs));
    }

    /// <summary>
    /// With no tenant resolved there is no scope to act in, so every operation refuses rather than falling
    /// back to "all tenants" — the failure mode that would turn one missing claim into a full data leak.
    /// </summary>
    [Fact]
    public async Task Without_a_tenant_every_operation_is_unauthorized()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        var existing = OrganizationTestHarness.DepartmentId(Demo01, "ENG");
        harness.ActAs(null);

        Assert.Equal(ResultStatus.Unauthorized, (await harness.Departments().GetAsync(new DepartmentQuery())).Status);
        Assert.Equal(ResultStatus.Unauthorized, (await harness.Departments().GetByIdAsync(existing)).Status);
        Assert.Equal(ResultStatus.Unauthorized, (await harness.Departments().CreateAsync(Request("QA", "Quality"))).Status);
        Assert.Equal(ResultStatus.Unauthorized, (await harness.Departments().UpdateAsync(existing, Request("ENG", "Engineering"))).Status);
        Assert.Equal(ResultStatus.Unauthorized, (await harness.Departments().DeleteAsync(existing)).Status);

        using var unscoped = harness.CreateUnscopedContext();
        Assert.Equal(5, await unscoped.Departments.IgnoreQueryFilters().CountAsync());
    }

    private static DepartmentRequest Request(
        string code, string name, string? description = null, bool isActive = true) => new()
    {
        Code = code,
        Name = name,
        Description = description,
        IsActive = isActive
    };
}
