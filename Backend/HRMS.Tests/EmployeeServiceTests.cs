using HRMS.Application.Common;
using HRMS.Application.DTOs.Departments;
using HRMS.Application.DTOs.Employees;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

/// <summary>
/// Employee behaviour. The interesting cases here are the ones a foreign key cannot catch on its own: an id
/// that exists, but in another organization. Those go through the service so the tenant filter is what
/// decides, and are then checked against the stored row so a rejected write really left nothing behind.
/// </summary>
public class EmployeeServiceTests
{
    private static readonly Guid Demo01 = SeedData.TenantIds.Demo01;
    private static readonly Guid Demo02 = SeedData.TenantIds.Demo02;

    private static readonly DateOnly Joined = new(2023, 5, 1);

    [Fact]
    public async Task Get_returns_only_the_callers_own_employees()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var mine = await harness.ActAs(Demo01).Employees().GetAsync(new EmployeeQuery());
        var theirs = await harness.ActAs(Demo02).Employees().GetAsync(new EmployeeQuery());

        Assert.Equal(6, mine.Value!.TotalCount);
        Assert.Equal(new[] { "E-100", "E-101" }, theirs.Value!.Items.Select(e => e.EmployeeCode));
        Assert.DoesNotContain("Nadia Farrell", theirs.Value.Items.Select(e => e.FullName));
    }

    [Fact]
    public async Task List_items_carry_the_joined_department_and_designation_names()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var page = await harness.Employees().GetAsync(new EmployeeQuery());
        var cto = page.Value!.Items.Single(e => e.EmployeeCode == "EMP-001");

        Assert.Equal("Nadia Farrell", cto.FullName);
        Assert.Equal("Engineering", cto.DepartmentName);
        Assert.Equal("Chief Technology Officer", cto.DesignationName);
        Assert.Equal(EmployeeStatus.Active, cto.Status);
    }

    [Fact]
    public async Task Get_filters_by_department_designation_status_and_manager()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var engineering = OrganizationTestHarness.DepartmentId(Demo01, "ENG");
        var seniorEngineer = OrganizationTestHarness.DesignationId(Demo01, "SSE");
        var manager = OrganizationTestHarness.EmployeeId(Demo01, "EMP-002");

        var byDepartment = await harness.Employees().GetAsync(new EmployeeQuery { DepartmentId = engineering });
        var byDesignation = await harness.Employees().GetAsync(new EmployeeQuery { DesignationId = seniorEngineer });
        var byManager = await harness.Employees().GetAsync(new EmployeeQuery { ReportingManagerId = manager });
        var resigned = await harness.Employees().GetAsync(new EmployeeQuery { Status = EmployeeStatus.Resigned });

        Assert.Equal(4, byDepartment.Value!.TotalCount);
        Assert.Equal(new[] { "EMP-003" }, byDesignation.Value!.Items.Select(e => e.EmployeeCode));
        Assert.Equal(new[] { "EMP-003", "EMP-004" }, byManager.Value!.Items.Select(e => e.EmployeeCode));
        Assert.Empty(resigned.Value!.Items);
    }

    [Fact]
    public async Task Search_matches_code_name_and_email()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var byCode = await harness.Employees().GetAsync(new EmployeeQuery { Search = "emp-005" });
        var byLastName = await harness.Employees().GetAsync(new EmployeeQuery { Search = "RAMAN" });
        var byEmail = await harness.Employees().GetAsync(new EmployeeQuery { Search = "tomas.lind@" });

        Assert.Equal(new[] { "EMP-005" }, byCode.Value!.Items.Select(e => e.EmployeeCode));
        Assert.Equal(new[] { "EMP-003" }, byLastName.Value!.Items.Select(e => e.EmployeeCode));
        Assert.Equal(new[] { "EMP-006" }, byEmail.Value!.Items.Select(e => e.EmployeeCode));
    }

    [Fact]
    public async Task GetById_includes_the_reporting_manager_name()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var employee = await harness.Employees().GetByIdAsync(OrganizationTestHarness.EmployeeId(Demo01, "EMP-003"));
        var top = await harness.Employees().GetByIdAsync(OrganizationTestHarness.EmployeeId(Demo01, "EMP-001"));

        Assert.Equal("Owen Brand", employee.Value!.ReportingManagerName);
        Assert.Null(top.Value!.ReportingManagerName);
    }

    [Fact]
    public async Task GetById_of_another_tenants_employee_is_not_found()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var theirs = OrganizationTestHarness.EmployeeId(Demo02, "E-100");
        var result = await harness.ActAs(Demo01).Employees().GetByIdAsync(theirs);

        Assert.Equal(ResultStatus.NotFound, result.Status);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task Create_stamps_the_authenticated_tenant_and_returns_the_joined_record()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var result = await harness.Employees().CreateAsync(NewRequest(harness));

        Assert.True(result.Succeeded);
        Assert.Equal("Sam Okafor", result.Value!.FullName);
        Assert.Equal("Engineering", result.Value.DepartmentName);
        Assert.Equal("Software Engineer", result.Value.DesignationName);
        Assert.Equal("Owen Brand", result.Value.ReportingManagerName);

        using var unscoped = harness.CreateUnscopedContext();
        var saved = await unscoped.Employees.IgnoreQueryFilters().SingleAsync(e => e.Id == result.Value.Id);
        Assert.Equal(Demo01, saved.TenantId);
    }

    /// <summary>
    /// The core write-side isolation case. The department id is real, so the foreign key would be satisfied —
    /// it just belongs to another organization, and the service resolves it through the tenant filter first.
    /// </summary>
    [Fact]
    public async Task Create_rejects_a_department_belonging_to_another_tenant()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var request = NewRequest(harness);
        request.DepartmentId = OrganizationTestHarness.DepartmentId(Demo02, "OPS");

        var result = await harness.Employees().CreateAsync(request);

        Assert.Equal(ResultStatus.ValidationFailed, result.Status);
        Assert.Equal("departmentId", Assert.Single(result.Errors!).Field);
        Assert.Contains("does not exist", result.Message);

        using var unscoped = harness.CreateUnscopedContext();
        Assert.Equal(8, await unscoped.Employees.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task Create_rejects_a_designation_belonging_to_another_tenant()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var request = NewRequest(harness);
        request.DesignationId = OrganizationTestHarness.DesignationId(Demo02, "SR");

        var result = await harness.Employees().CreateAsync(request);

        Assert.Equal(ResultStatus.ValidationFailed, result.Status);
        Assert.Equal("designationId", Assert.Single(result.Errors!).Field);
    }

    [Fact]
    public async Task Create_rejects_a_manager_belonging_to_another_tenant()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var request = NewRequest(harness);
        request.ReportingManagerId = OrganizationTestHarness.EmployeeId(Demo02, "E-100");

        var result = await harness.Employees().CreateAsync(request);

        Assert.Equal(ResultStatus.ValidationFailed, result.Status);
        Assert.Equal("reportingManagerId", Assert.Single(result.Errors!).Field);
    }

    [Fact]
    public async Task Create_rejects_a_nonexistent_department()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var request = NewRequest(harness);
        request.DepartmentId = Guid.NewGuid();

        var result = await harness.Employees().CreateAsync(request);

        Assert.Equal(ResultStatus.ValidationFailed, result.Status);
        Assert.Equal("departmentId", Assert.Single(result.Errors!).Field);
    }

    [Fact]
    public async Task Create_rejects_a_retired_department()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        await RetireEngineeringAsync(harness);

        var result = await harness.Employees().CreateAsync(NewRequest(harness));

        Assert.Equal(ResultStatus.ValidationFailed, result.Status);
        Assert.Equal("departmentId", Assert.Single(result.Errors!).Field);
        Assert.Contains("no longer active", result.Message);
    }

    [Fact]
    public async Task Create_rejects_a_manager_who_has_left_the_organization()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var leaver = await ResignAsync(harness, "EMP-004");

        var request = NewRequest(harness);
        request.ReportingManagerId = leaver;

        var result = await harness.Employees().CreateAsync(request);

        Assert.Equal(ResultStatus.ValidationFailed, result.Status);
        Assert.Equal("reportingManagerId", Assert.Single(result.Errors!).Field);
        Assert.Contains("has left the organization", result.Message);
    }

    [Fact]
    public async Task Create_rejects_a_duplicate_employee_code_regardless_of_case()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var request = NewRequest(harness);
        request.EmployeeCode = "emp-003";

        var result = await harness.Employees().CreateAsync(request);

        Assert.Equal(ResultStatus.Conflict, result.Status);
        Assert.Equal("employeeCode", Assert.Single(result.Errors!).Field);
    }

    [Fact]
    public async Task Create_rejects_a_duplicate_email_regardless_of_case()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var request = NewRequest(harness);
        request.Email = "PRIYA.RAMAN@demo01.com";

        var result = await harness.Employees().CreateAsync(request);

        Assert.Equal(ResultStatus.Conflict, result.Status);
        Assert.Equal("email", Assert.Single(result.Errors!).Field);
    }

    /// <summary>
    /// Uniqueness is per tenant. Two organizations may each employ someone with the code "EMP-001", and one
    /// tenant's directory must not constrain another's.
    /// </summary>
    [Fact]
    public async Task Another_tenant_may_reuse_an_employee_code_and_email()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        harness.ActAs(Demo02);

        var request = new EmployeeRequest
        {
            EmployeeCode = "EMP-001",
            FirstName = "Ada",
            LastName = "Nwosu",
            Email = "nadia.farrell@demo01.com",
            DateOfJoining = Joined,
            DepartmentId = OrganizationTestHarness.DepartmentId(Demo02, "OPS"),
            DesignationId = OrganizationTestHarness.DesignationId(Demo02, "SR")
        };

        var result = await harness.Employees().CreateAsync(request);

        Assert.True(result.Succeeded);

        using var unscoped = harness.CreateUnscopedContext();
        Assert.Equal(2, await unscoped.Employees.IgnoreQueryFilters().CountAsync(e => e.EmployeeCode == "EMP-001"));
    }

    [Fact]
    public async Task Update_replaces_the_record_and_stamps_a_modified_date()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var id = OrganizationTestHarness.EmployeeId(Demo01, "EMP-003");
        var existing = (await harness.Employees().GetByIdAsync(id)).Value!;

        var request = ToRequest(existing);
        request.Phone = "555-9999";
        request.Address = null;

        var result = await harness.Employees().UpdateAsync(id, request);

        Assert.True(result.Succeeded);
        Assert.Equal("555-9999", result.Value!.Phone);
        Assert.Null(result.Value.Address);
        Assert.NotNull(result.Value.ModifiedDate);
    }

    [Fact]
    public async Task Update_of_another_tenants_employee_is_not_found_and_changes_nothing()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var theirs = OrganizationTestHarness.EmployeeId(Demo02, "E-101");
        var request = NewRequest(harness);

        var result = await harness.ActAs(Demo01).Employees().UpdateAsync(theirs, request);

        Assert.Equal(ResultStatus.NotFound, result.Status);

        using var unscoped = harness.CreateUnscopedContext();
        var untouched = await unscoped.Employees.IgnoreQueryFilters().SingleAsync(e => e.Id == theirs);
        Assert.Equal("Liam", untouched.FirstName);
        Assert.Equal(Demo02, untouched.TenantId);
        Assert.Null(untouched.ModifiedDate);
    }

    /// <summary>
    /// Editing an employee whose department was retired must stay possible — otherwise retiring a unit would
    /// freeze the records of everyone who ever worked in it.
    /// </summary>
    [Fact]
    public async Task Update_may_keep_a_department_that_has_since_been_retired()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var id = OrganizationTestHarness.EmployeeId(Demo01, "EMP-003");
        var existing = (await harness.Employees().GetByIdAsync(id)).Value!;
        await RetireEngineeringAsync(harness);

        var request = ToRequest(existing);
        request.Phone = "555-1234";

        var result = await harness.Employees().UpdateAsync(id, request);

        Assert.True(result.Succeeded);
        Assert.Equal("555-1234", result.Value!.Phone);
    }

    [Fact]
    public async Task Update_rejects_a_move_into_a_retired_department()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var finance = OrganizationTestHarness.DepartmentId(Demo01, "FIN");
        await harness.Departments().UpdateAsync(finance, new DepartmentRequest
        {
            Code = "FIN",
            Name = "Finance",
            IsActive = false
        });

        var id = OrganizationTestHarness.EmployeeId(Demo01, "EMP-003");
        var request = ToRequest((await harness.Employees().GetByIdAsync(id)).Value!);
        request.DepartmentId = finance;

        var result = await harness.Employees().UpdateAsync(id, request);

        Assert.Equal(ResultStatus.ValidationFailed, result.Status);
        Assert.Equal("departmentId", Assert.Single(result.Errors!).Field);
    }

    [Fact]
    public async Task Update_rejects_an_employee_reporting_to_themselves()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var id = OrganizationTestHarness.EmployeeId(Demo01, "EMP-003");
        var request = ToRequest((await harness.Employees().GetByIdAsync(id)).Value!);
        request.ReportingManagerId = id;

        var result = await harness.Employees().UpdateAsync(id, request);

        Assert.Equal(ResultStatus.ValidationFailed, result.Status);
        Assert.Equal("reportingManagerId", Assert.Single(result.Errors!).Field);
        Assert.Contains("cannot report to themselves", result.Message);
    }

    /// <summary>
    /// EMP-003 reports to EMP-002, who reports to EMP-001. Making EMP-003 the manager of EMP-001 would close
    /// that line into a loop, which no amount of foreign-key checking would notice.
    /// </summary>
    [Fact]
    public async Task Update_rejects_a_manager_who_reports_up_through_the_employee()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var top = OrganizationTestHarness.EmployeeId(Demo01, "EMP-001");
        var request = ToRequest((await harness.Employees().GetByIdAsync(top)).Value!);
        request.ReportingManagerId = OrganizationTestHarness.EmployeeId(Demo01, "EMP-003");

        var result = await harness.Employees().UpdateAsync(top, request);

        Assert.Equal(ResultStatus.ValidationFailed, result.Status);
        Assert.Equal("reportingManagerId", Assert.Single(result.Errors!).Field);
        Assert.Contains("loop", result.Message);

        using var unscoped = harness.CreateUnscopedContext();
        var untouched = await unscoped.Employees.IgnoreQueryFilters().SingleAsync(e => e.Id == top);
        Assert.Null(untouched.ReportingManagerId);
    }

    [Fact]
    public async Task Update_accepts_a_manager_change_that_keeps_the_hierarchy_a_tree()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var id = OrganizationTestHarness.EmployeeId(Demo01, "EMP-004");
        var request = ToRequest((await harness.Employees().GetByIdAsync(id)).Value!);
        request.ReportingManagerId = OrganizationTestHarness.EmployeeId(Demo01, "EMP-005");

        var result = await harness.Employees().UpdateAsync(id, request);

        Assert.True(result.Succeeded);
        Assert.Equal("Mira Kovac", result.Value!.ReportingManagerName);
    }

    /// <summary>
    /// A manager who resigns is not detached from their reports, so editing one of those reports must not be
    /// blocked by the manager's status — re-parenting an org chart is a deliberate act, not a side effect.
    /// </summary>
    [Fact]
    public async Task Update_keeps_an_existing_manager_who_has_since_left()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var id = OrganizationTestHarness.EmployeeId(Demo01, "EMP-003");
        var existing = (await harness.Employees().GetByIdAsync(id)).Value!;
        await ResignAsync(harness, "EMP-002");

        var request = ToRequest(existing);
        request.Phone = "555-4321";

        var result = await harness.Employees().UpdateAsync(id, request);

        Assert.True(result.Succeeded);
        Assert.Equal("Owen Brand", result.Value!.ReportingManagerName);
    }

    [Fact]
    public async Task Update_rejects_an_employee_code_another_employee_already_uses()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var id = OrganizationTestHarness.EmployeeId(Demo01, "EMP-003");
        var request = ToRequest((await harness.Employees().GetByIdAsync(id)).Value!);
        request.EmployeeCode = "EMP-004";

        var result = await harness.Employees().UpdateAsync(id, request);

        Assert.Equal(ResultStatus.Conflict, result.Status);
        Assert.Equal("employeeCode", Assert.Single(result.Errors!).Field);
    }

    [Fact]
    public async Task Delete_refuses_an_employee_who_still_has_direct_reports()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var top = OrganizationTestHarness.EmployeeId(Demo01, "EMP-001");
        var result = await harness.Employees().DeleteAsync(top);

        Assert.Equal(ResultStatus.Conflict, result.Status);
        Assert.Contains("3 employee(s)", result.Message);

        using var unscoped = harness.CreateUnscopedContext();
        Assert.True(await unscoped.Employees.IgnoreQueryFilters().AnyAsync(e => e.Id == top));
    }

    [Fact]
    public async Task Delete_removes_an_employee_nobody_reports_to()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var leaf = OrganizationTestHarness.EmployeeId(Demo01, "EMP-004");
        var result = await harness.Employees().DeleteAsync(leaf);

        Assert.True(result.Succeeded);

        using var unscoped = harness.CreateUnscopedContext();
        Assert.False(await unscoped.Employees.IgnoreQueryFilters().AnyAsync(e => e.Id == leaf));
    }

    [Fact]
    public async Task Delete_of_another_tenants_employee_is_not_found()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var theirs = OrganizationTestHarness.EmployeeId(Demo02, "E-101");
        var result = await harness.ActAs(Demo01).Employees().DeleteAsync(theirs);

        Assert.Equal(ResultStatus.NotFound, result.Status);

        using var unscoped = harness.CreateUnscopedContext();
        Assert.True(await unscoped.Employees.IgnoreQueryFilters().AnyAsync(e => e.Id == theirs));
    }

    [Fact]
    public async Task Without_a_tenant_every_operation_is_unauthorized()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        var request = NewRequest(harness);
        var existing = OrganizationTestHarness.EmployeeId(Demo01, "EMP-003");
        harness.ActAs(null);

        Assert.Equal(ResultStatus.Unauthorized, (await harness.Employees().GetAsync(new EmployeeQuery())).Status);
        Assert.Equal(ResultStatus.Unauthorized, (await harness.Employees().GetByIdAsync(existing)).Status);
        Assert.Equal(ResultStatus.Unauthorized, (await harness.Employees().CreateAsync(request)).Status);
        Assert.Equal(ResultStatus.Unauthorized, (await harness.Employees().UpdateAsync(existing, request)).Status);
        Assert.Equal(ResultStatus.Unauthorized, (await harness.Employees().DeleteAsync(existing)).Status);
        Assert.Equal(ResultStatus.Unauthorized, (await harness.Employees().ExportAsync(new EmployeeQuery())).Status);

        using var unscoped = harness.CreateUnscopedContext();
        Assert.Equal(8, await unscoped.Employees.IgnoreQueryFilters().CountAsync());
    }

    /// <summary>A valid DEMO01 hire: Engineering, Software Engineer, reporting to the engineering manager.</summary>
    private static EmployeeRequest NewRequest(OrganizationTestHarness harness) => new()
    {
        EmployeeCode = "EMP-007",
        FirstName = "Sam",
        LastName = "Okafor",
        Email = "sam.okafor@demo01.com",
        Phone = "555-0107",
        DateOfBirth = new DateOnly(1994, 3, 3),
        Gender = Gender.Other,
        DateOfJoining = Joined,
        Status = EmployeeStatus.Active,
        DepartmentId = OrganizationTestHarness.DepartmentId(Demo01, "ENG"),
        DesignationId = OrganizationTestHarness.DesignationId(Demo01, "SE"),
        ReportingManagerId = OrganizationTestHarness.EmployeeId(Demo01, "EMP-002"),
        Address = "1 New Street"
    };

    /// <summary>
    /// Turns a stored record back into the equivalent write request, so an update test changes exactly one
    /// field of a full replacement instead of silently clearing the rest.
    /// </summary>
    private static EmployeeRequest ToRequest(EmployeeDto dto) => new()
    {
        EmployeeCode = dto.EmployeeCode,
        FirstName = dto.FirstName,
        LastName = dto.LastName,
        Email = dto.Email,
        Phone = dto.Phone,
        DateOfBirth = dto.DateOfBirth,
        Gender = dto.Gender,
        DateOfJoining = dto.DateOfJoining,
        DateOfLeaving = dto.DateOfLeaving,
        Status = dto.Status,
        DepartmentId = dto.DepartmentId,
        DesignationId = dto.DesignationId,
        ReportingManagerId = dto.ReportingManagerId,
        Address = dto.Address
    };

    private static async Task RetireEngineeringAsync(OrganizationTestHarness harness)
    {
        var engineering = OrganizationTestHarness.DepartmentId(Demo01, "ENG");
        var result = await harness.Departments().UpdateAsync(engineering, new DepartmentRequest
        {
            Code = "ENG",
            Name = "Engineering",
            IsActive = false
        });

        Assert.True(result.Succeeded);
    }

    /// <summary>Marks a seeded employee as resigned and returns their id.</summary>
    private static async Task<Guid> ResignAsync(OrganizationTestHarness harness, string employeeCode)
    {
        var id = OrganizationTestHarness.EmployeeId(Demo01, employeeCode);

        var request = ToRequest((await harness.Employees().GetByIdAsync(id)).Value!);
        request.Status = EmployeeStatus.Resigned;
        request.DateOfLeaving = new DateOnly(2026, 6, 30);

        var result = await harness.Employees().UpdateAsync(id, request);
        Assert.True(result.Succeeded);

        return id;
    }
}
