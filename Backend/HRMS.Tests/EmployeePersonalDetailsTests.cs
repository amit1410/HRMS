using HRMS.Application.Common;
using HRMS.Application.DTOs.Employees;
using HRMS.Application.Validators.Employees;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

/// <summary>
/// The Employee → Personal Details flow. These cover the behaviour that is specific to the Personal Details
/// creation path: employee code assignment from the tenant configuration, creating an employee with no
/// department/designation (which belong to a later section), and ESIC applicability.
/// </summary>
public class EmployeePersonalDetailsTests
{
    private static readonly Guid Demo01 = SeedData.TenantIds.Demo01;

    private static readonly DateOnly Joined = new(2023, 5, 1);

    [Fact]
    public async Task Create_leaves_auto_employee_code_pending_until_initial_employment()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        using (var arrange = harness.CreateContext())
        {
            arrange.EmployeeCodeConfigs.Add(new EmployeeCodeConfig
            {
                Id = Guid.NewGuid(),
                TenantId = Demo01,
                AutoGenerate = true,
                Prefix = "WE",
                NextNumber = 100,
                Padding = 3
            });
            await arrange.SaveChangesAsync();
        }

        var result = await harness.Employees().CreatePersonalDetailsAsync(NewRequest());

        Assert.True(result.Succeeded);
        Assert.Equal(string.Empty, result.Value!.EmployeeCode);

        using var unscoped = harness.CreateUnscopedContext();
        var saved = await unscoped.Employees.IgnoreQueryFilters().SingleAsync(e => e.Id == result.Value.Id);
        Assert.Equal(Demo01, saved.TenantId);
        Assert.Null(saved.EmployeeCode);
    }

    [Fact]
    public async Task Create_does_not_consume_employee_code_sequence()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        using (var arrange = harness.CreateContext())
        {
            arrange.EmployeeCodeConfigs.Add(new EmployeeCodeConfig
            {
                Id = Guid.NewGuid(),
                TenantId = Demo01,
                AutoGenerate = true,
                Prefix = "EMP",
                NextNumber = 5,
                Padding = 4
            });
            await arrange.SaveChangesAsync();
        }

        var first = await harness.Employees().CreatePersonalDetailsAsync(NewRequest());
        var second = await harness.Employees().CreatePersonalDetailsAsync(NewRequest());

        Assert.Equal(string.Empty, first.Value!.EmployeeCode);
        Assert.Equal(string.Empty, second.Value!.EmployeeCode);
    }

    [Fact]
    public async Task Create_uses_the_default_configuration_when_none_exists()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var result = await harness.Employees().CreatePersonalDetailsAsync(NewRequest());

        Assert.True(result.Succeeded);
        Assert.Equal(string.Empty, result.Value!.EmployeeCode);
    }

    [Fact]
    public async Task Create_creates_with_no_department_or_designation()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var result = await harness.Employees().CreatePersonalDetailsAsync(NewRequest());

        Assert.True(result.Succeeded);
        Assert.Null(result.Value!.DepartmentId);
        Assert.Null(result.Value.DesignationId);
        Assert.Null(result.Value.ReportingManagerId);

        using var unscoped = harness.CreateUnscopedContext();
        var saved = await unscoped.Employees.IgnoreQueryFilters().SingleAsync(e => e.Id == result.Value.Id);
        Assert.Null(saved.DepartmentId);
        Assert.Null(saved.DesignationId);
    }

    [Fact]
    public async Task Create_requires_esic_number_when_esic_is_applicable()
    {
        var validator = new EmployeePersonalDetailsRequestValidator();

        var request = NewRequest();
        request.EsicApplicable = true;
        request.EsicNumber = null;

        var result = await validator.ValidateAsync(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(request.EsicNumber));
    }

    [Fact]
    public async Task Update_updates_only_personal_fields_on_an_existing_employee()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        // Start with a real employee that already has a department and designation.
        var id = OrganizationTestHarness.EmployeeId(Demo01, "EMP-003");
        var existing = (await harness.Employees().GetByIdAsync(id)).Value!;

        var request = NewRequest();
        request.FirstName = "Renamed";
        request.MaritalStatus = MaritalStatus.Married;

        var result = await harness.Employees().UpdatePersonalDetailsAsync(id, request);

        Assert.True(result.Succeeded);
        Assert.Equal("Renamed", result.Value!.FirstName);
        Assert.Equal(MaritalStatus.Married, result.Value!.MaritalStatus);
        // Employment/position fields are untouched by a Personal Details update.
        Assert.Equal(existing.DepartmentId, result.Value.DepartmentId);
        Assert.Equal(existing.DesignationId, result.Value.DesignationId);
    }

    [Fact]
    public async Task Update_of_another_tenants_employee_is_not_found()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var theirs = OrganizationTestHarness.EmployeeId(SeedData.TenantIds.Demo02, "E-101");
        var result = await harness.ActAs(Demo01).Employees().UpdatePersonalDetailsAsync(theirs, NewRequest());

        Assert.Equal(ResultStatus.NotFound, result.Status);
    }

    private static EmployeePersonalDetailsRequest NewRequest() => new()
    {
        Salutation = "Mr.",
        FirstName = "Sam",
        MiddleName = "O.",
        LastName = "Okafor",
        DateOfBirth = new DateOnly(1994, 3, 3),
        Gender = Gender.Male,
        BloodGroup = BloodGroup.OPositive,
        MaritalStatus = MaritalStatus.Single,
        Religion = "Christian",
        Caste = "Igbo",
        Citizenship = "Nigeria",
        DateOfJoining = Joined
    };
}
