using HRMS.Application.Common;
using HRMS.Application.DTOs.Designations;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

/// <summary>
/// Designation behaviour. Deliberately slimmer than <see cref="DepartmentServiceTests"/>: the two services
/// are mirror images, so this covers the isolation and conflict paths that would break independently rather
/// than restating every filter and trim case.
/// </summary>
public class DesignationServiceTests
{
    private static readonly Guid Demo01 = SeedData.TenantIds.Demo01;
    private static readonly Guid Demo02 = SeedData.TenantIds.Demo02;

    [Fact]
    public async Task Get_returns_only_the_callers_own_designations()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var mine = await harness.ActAs(Demo01).Designations().GetAsync(new DesignationQuery());
        var theirs = await harness.ActAs(Demo02).Designations().GetAsync(new DesignationQuery());

        Assert.Equal(6, mine.Value!.TotalCount);
        Assert.Equal(new[] { "OPSM", "SR" }, theirs.Value!.Items.Select(d => d.Code));
        Assert.DoesNotContain("CTO", theirs.Value.Items.Select(d => d.Code));
    }

    [Fact]
    public async Task Employee_count_reports_how_many_people_hold_the_title()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var unheld = (await harness.Designations().CreateAsync(Request("QA", "QA Engineer"))).Value!;

        var designations = (await harness.Designations().GetAsync(new DesignationQuery())).Value!.Items;

        Assert.Equal(1, designations.Single(d => d.Code == "CTO").EmployeeCount);
        Assert.Equal(1, designations.Single(d => d.Code == "SSE").EmployeeCount);
        Assert.Equal(0, designations.Single(d => d.Id == unheld.Id).EmployeeCount);
    }

    [Fact]
    public async Task GetById_of_another_tenants_designation_is_not_found()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var theirs = OrganizationTestHarness.DesignationId(Demo02, "OPSM");
        var result = await harness.ActAs(Demo01).Designations().GetByIdAsync(theirs);

        Assert.Equal(ResultStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task Create_stamps_the_authenticated_tenant()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var result = await harness.Designations().CreateAsync(Request("QA", "QA Engineer"));

        Assert.True(result.Succeeded);

        using var unscoped = harness.CreateUnscopedContext();
        var saved = await unscoped.Designations.IgnoreQueryFilters().SingleAsync(d => d.Id == result.Value!.Id);
        Assert.Equal(Demo01, saved.TenantId);
    }

    [Fact]
    public async Task Create_rejects_a_duplicate_code_regardless_of_case()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var result = await harness.Designations().CreateAsync(Request("cto", "Chief Tech"));

        Assert.Equal(ResultStatus.Conflict, result.Status);
        Assert.Equal("code", Assert.Single(result.Errors!).Field);
    }

    [Fact]
    public async Task Create_rejects_a_duplicate_name()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var result = await harness.Designations().CreateAsync(Request("CTO2", "chief technology officer"));

        Assert.Equal(ResultStatus.Conflict, result.Status);
        Assert.Equal("name", Assert.Single(result.Errors!).Field);
    }

    [Fact]
    public async Task Update_of_another_tenants_designation_is_not_found_and_changes_nothing()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var theirs = OrganizationTestHarness.DesignationId(Demo02, "SR");
        var result = await harness.ActAs(Demo01).Designations().UpdateAsync(theirs, Request("SR", "Hijacked"));

        Assert.Equal(ResultStatus.NotFound, result.Status);

        using var unscoped = harness.CreateUnscopedContext();
        var untouched = await unscoped.Designations.IgnoreQueryFilters().SingleAsync(d => d.Id == theirs);
        Assert.Equal("Sales Representative", untouched.Name);
    }

    [Fact]
    public async Task Delete_refuses_a_designation_that_employees_still_hold()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var cto = OrganizationTestHarness.DesignationId(Demo01, "CTO");
        var result = await harness.Designations().DeleteAsync(cto);

        Assert.Equal(ResultStatus.Conflict, result.Status);
        Assert.Contains("1 employee(s)", result.Message);

        using var unscoped = harness.CreateUnscopedContext();
        Assert.True(await unscoped.Designations.IgnoreQueryFilters().AnyAsync(d => d.Id == cto));
    }

    [Fact]
    public async Task Delete_removes_a_designation_nobody_holds()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var created = (await harness.Designations().CreateAsync(Request("QA", "QA Engineer"))).Value!;
        var result = await harness.Designations().DeleteAsync(created.Id);

        Assert.True(result.Succeeded);

        using var unscoped = harness.CreateUnscopedContext();
        Assert.False(await unscoped.Designations.IgnoreQueryFilters().AnyAsync(d => d.Id == created.Id));
    }

    [Fact]
    public async Task Without_a_tenant_every_operation_is_unauthorized()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        var existing = OrganizationTestHarness.DesignationId(Demo01, "CTO");
        harness.ActAs(null);

        Assert.Equal(ResultStatus.Unauthorized, (await harness.Designations().GetAsync(new DesignationQuery())).Status);
        Assert.Equal(ResultStatus.Unauthorized, (await harness.Designations().GetByIdAsync(existing)).Status);
        Assert.Equal(ResultStatus.Unauthorized, (await harness.Designations().CreateAsync(Request("QA", "QA Engineer"))).Status);
        Assert.Equal(ResultStatus.Unauthorized, (await harness.Designations().UpdateAsync(existing, Request("CTO", "Chief Technology Officer"))).Status);
        Assert.Equal(ResultStatus.Unauthorized, (await harness.Designations().DeleteAsync(existing)).Status);

        using var unscoped = harness.CreateUnscopedContext();
        Assert.Equal(8, await unscoped.Designations.IgnoreQueryFilters().CountAsync());
    }

    private static DesignationRequest Request(
        string code, string name, string? description = null, bool isActive = true) => new()
    {
        Code = code,
        Name = name,
        Description = description,
        IsActive = isActive
    };
}
