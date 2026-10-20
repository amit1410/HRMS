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
    public async Task Create_personal_details_does_not_require_sensitive_edit_permission()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var result = await harness.Employees().CreatePersonalDetailsAsync(
            NewRequest(),
            canEditSensitive: false);

        Assert.True(result.Succeeded);
        Assert.Equal(string.Empty, result.Value!.EmployeeCode);
        Assert.Equal("Sam", result.Value.FirstName);
    }

    [Fact]
    public async Task Create_personal_details_without_job_status_is_rejected()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        var request = NewRequest();
        request.JobStatus = null;

        var result = await harness.Employees().CreatePersonalDetailsAsync(request);

        Assert.Equal(ResultStatus.ValidationFailed, result.Status);
        Assert.Contains(result.Errors!, e => e.Field == "jobStatus");
    }

    [Fact]
    public async Task Create_personal_details_rejects_invalid_aadhaar_and_pan()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        var request = NewRequest();
        request.AadhaarNumber = "123ABC";
        request.PanNumber = "BAD";

        var result = await harness.Employees().CreatePersonalDetailsAsync(request);

        Assert.Equal(ResultStatus.ValidationFailed, result.Status);
        Assert.Contains(result.Errors!, e => e.Field == "aadhaarNumber");
        Assert.Contains(result.Errors!, e => e.Field == "panNumber");
    }

    [Fact]
    public async Task Create_personal_details_normalizes_pan_to_uppercase()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        var request = NewRequest();
        request.PanNumber = "abcde1234f";

        var result = await harness.Employees().CreatePersonalDetailsAsync(request);

        Assert.True(result.Succeeded, result.Message);
        using var read = harness.CreateUnscopedContext();
        Assert.Equal("ABCDE1234F", await read.Employees.IgnoreQueryFilters()
            .Where(e => e.Id == result.Value!.Id).Select(e => e.PanNumber).SingleAsync());
    }

    [Fact]
    public async Task Create_without_sensitive_permission_does_not_persist_sensitive_values_but_authorized_create_does()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        var request = NewRequest();
        request.AadhaarNumber = "111122223333";
        request.PanNumber = "ABCDE1234F";
        request.PfNumber = "PF-TEST-0001";
        request.UanNumber = "999988887777";
        request.EsicApplicable = true;
        request.EsicNumber = "ESIC-000123";
        request.MediclaimNumber = "MED-000456";

        var unauthorized = await harness.Employees().CreatePersonalDetailsAsync(request, canEditSensitive: false);
        Assert.True(unauthorized.Succeeded, unauthorized.Message);
        using (var read = harness.CreateUnscopedContext())
        {
            var saved = await read.Employees.IgnoreQueryFilters().SingleAsync(e => e.Id == unauthorized.Value!.Id);
            Assert.Null(saved.AadhaarNumber);
            Assert.Null(saved.PanNumber);
            Assert.Null(saved.PfNumber);
            Assert.Null(saved.UanNumber);
            Assert.Null(saved.EsicNumber);
            Assert.Null(saved.MediclaimNumber);
            Assert.False(saved.EsicApplicable);
        }

        request.FirstName = "Authorized";
        var authorized = await harness.Employees().CreatePersonalDetailsAsync(request, canEditSensitive: true);
        Assert.True(authorized.Succeeded, authorized.Message);
        using var authorizedRead = harness.CreateUnscopedContext();
        var authorizedSaved = await authorizedRead.Employees.IgnoreQueryFilters()
            .SingleAsync(e => e.Id == authorized.Value!.Id);
        Assert.Equal(request.AadhaarNumber, authorizedSaved.AadhaarNumber);
        Assert.Equal(request.PanNumber, authorizedSaved.PanNumber);
        Assert.Equal(request.PfNumber, authorizedSaved.PfNumber);
        Assert.Equal(request.UanNumber, authorizedSaved.UanNumber);
        Assert.Equal(request.EsicNumber, authorizedSaved.EsicNumber);
        Assert.Equal(request.MediclaimNumber, authorizedSaved.MediclaimNumber);
        Assert.True(authorizedSaved.EsicApplicable);
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

    [Fact]
    public async Task Update_rejects_when_required_values_are_missing_from_the_effective_record()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        var created = (await harness.Employees().CreatePersonalDetailsAsync(NewRequest())).Value!;

        using (var arrange = harness.CreateContext())
        {
            var stored = await arrange.Employees.SingleAsync(e => e.Id == created.Id);
            stored.JobStatus = null;
            stored.AadhaarNumber = null;
            stored.PanNumber = null;
            await arrange.SaveChangesAsync();
        }

        var request = NewRequest();
        request.JobStatus = null;
        request.AadhaarNumber = null;
        request.PanNumber = null;
        var result = await harness.Employees().UpdatePersonalDetailsAsync(created.Id, request);

        Assert.Equal(ResultStatus.ValidationFailed, result.Status);
        Assert.Contains(result.Errors!, e => e.Field == "jobStatus");
        Assert.Contains(result.Errors!, e => e.Field == "aadhaarNumber");
        Assert.Contains(result.Errors!, e => e.Field == "panNumber");
    }

    [Fact]
    public async Task Update_preserves_valid_identifiers_and_normalizes_new_pan()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        var created = (await harness.Employees().CreatePersonalDetailsAsync(NewRequest())).Value!;
        var request = NewRequest();
        request.AadhaarNumber = null;
        request.PanNumber = "abcde1234f";

        var result = await harness.Employees().UpdatePersonalDetailsAsync(created.Id, request);

        Assert.True(result.Succeeded, result.Message);
        using var read = harness.CreateUnscopedContext();
        var stored = await read.Employees.IgnoreQueryFilters().SingleAsync(e => e.Id == created.Id);
        Assert.Equal("111122223333", stored.AadhaarNumber);
        Assert.Equal("ABCDE1234F", stored.PanNumber);
    }

    [Fact]
    public async Task Update_preserves_esic_when_blank_and_persists_a_changed_value()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        var create = NewRequest();
        create.EsicApplicable = true;
        create.EsicNumber = "ESIC-000123";
        var created = (await harness.Employees().CreatePersonalDetailsAsync(create)).Value!;

        var unrelated = NewRequest();
        unrelated.EsicApplicable = true;
        unrelated.EsicNumber = null;
        var preserved = await harness.Employees().UpdatePersonalDetailsAsync(created.Id, unrelated);
        Assert.True(preserved.Succeeded, preserved.Message);

        var changed = NewRequest();
        changed.EsicApplicable = true;
        changed.EsicNumber = "ESIC-000456";
        var updated = await harness.Employees().UpdatePersonalDetailsAsync(created.Id, changed);
        Assert.True(updated.Succeeded, updated.Message);

        using var read = harness.CreateUnscopedContext();
        var stored = await read.Employees.IgnoreQueryFilters().SingleAsync(e => e.Id == created.Id);
        Assert.Equal("ESIC-000456", stored.EsicNumber);
    }

    [Fact]
    public async Task Update_accepts_matching_masks_but_never_persists_them()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        var created = (await harness.Employees().CreatePersonalDetailsAsync(NewRequest())).Value!;
        var request = NewRequest();
        request.AadhaarNumber = "XXXX-XXXX-3333";
        request.PanNumber = "A****F";

        var result = await harness.Employees().UpdatePersonalDetailsAsync(created.Id, request);

        Assert.True(result.Succeeded, result.Message);
        using var read = harness.CreateUnscopedContext();
        var stored = await read.Employees.IgnoreQueryFilters().SingleAsync(e => e.Id == created.Id);
        Assert.Equal("111122223333", stored.AadhaarNumber);
        Assert.Equal("ABCDE1234F", stored.PanNumber);
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
        DateOfJoining = Joined,
        JobStatus = "Probation",
        AadhaarNumber = "111122223333",
        PanNumber = "ABCDE1234F"
    };
}
