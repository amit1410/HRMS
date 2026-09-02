using HRMS.Application.Common;
using HRMS.Application.DTOs.Employees;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public class EmployeeContactSynchronizationTests
{
    private static readonly Guid Demo01 = SeedData.TenantIds.Demo01;
    private static readonly Guid Demo02 = SeedData.TenantIds.Demo02;

    [Fact]
    public async Task Get_uses_legacy_employee_values_when_the_contact_row_does_not_exist()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        var employeeId = OrganizationTestHarness.EmployeeId(Demo01, "EMP-001");

        var result = await harness.Contacts().GetAsync(employeeId);

        Assert.True(result.Succeeded);
        Assert.Equal(Guid.Empty, result.Value!.Id);
        Assert.Equal("nadia.farrell@demo01.com", result.Value.OfficialEmail);
        Assert.NotNull(result.Value.OfficialPhone);
    }

    [Fact]
    public async Task Create_employee_then_save_and_update_contact_keeps_employee_reads_consistent()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        var createdEmployee = await harness.Employees().CreatePersonalDetailsAsync(new EmployeePersonalDetailsRequest
        {
            FirstName = "New",
            LastName = "Contact",
            DateOfJoining = new DateOnly(2026, 8, 31)
        });
        Assert.True(createdEmployee.Succeeded);
        var employeeId = createdEmployee.Value!.Id;

        var created = await harness.Contacts().UpsertAsync(employeeId, new EmployeeContactRequest
        {
            OfficialEmail = "priya.work@demo01.com",
            OfficialPhone = "555-1000",
            PersonalEmail = "priya.personal@example.com",
            PersonalPhone = "555-1001",
            AlternateEmail = "priya.alternate@example.com",
            EmergencyNumber = "555-1099"
        });
        var employeeAfterCreate = await harness.Employees().GetByIdAsync(employeeId);

        Assert.True(created.Succeeded);
        Assert.NotEqual(Guid.Empty, created.Value!.Id);
        Assert.Equal("priya.work@demo01.com", employeeAfterCreate.Value!.Email);
        Assert.Equal("555-1000", employeeAfterCreate.Value.Phone);

        var updated = await harness.Contacts().UpsertAsync(employeeId, new EmployeeContactRequest
        {
            OfficialEmail = "priya.updated@demo01.com",
            OfficialPhone = "555-2000",
            PersonalEmail = "priya.new-personal@example.com",
            PersonalPhone = "555-2001",
            AlternateEmail = "priya.new-alternate@example.com",
            EmergencyNumber = "555-2099"
        });
        var contactAfterUpdate = await harness.Contacts().GetAsync(employeeId);
        var employeeAfterUpdate = await harness.Employees().GetByIdAsync(employeeId);

        Assert.True(updated.Succeeded);
        Assert.Equal(created.Value.Id, updated.Value!.Id);
        Assert.Equal("priya.updated@demo01.com", contactAfterUpdate.Value!.OfficialEmail);
        Assert.Equal("555-2000", contactAfterUpdate.Value.OfficialPhone);
        Assert.Equal("priya.new-personal@example.com", contactAfterUpdate.Value.PersonalEmail);
        Assert.Equal("555-2001", contactAfterUpdate.Value.PersonalPhone);
        Assert.Equal("priya.new-alternate@example.com", contactAfterUpdate.Value.AlternateEmail);
        Assert.Equal("555-2099", contactAfterUpdate.Value.EmergencyNumber);
        Assert.Equal(contactAfterUpdate.Value.OfficialEmail, employeeAfterUpdate.Value!.Email);
        Assert.Equal(contactAfterUpdate.Value.OfficialPhone, employeeAfterUpdate.Value.Phone);

        using var unscoped = harness.CreateUnscopedContext();
        var storedEmployee = await unscoped.Employees.IgnoreQueryFilters().SingleAsync(e => e.Id == employeeId);
        var storedContact = await unscoped.EmployeeContacts.IgnoreQueryFilters()
            .SingleAsync(c => c.EmployeeId == employeeId);
        Assert.Equal(storedContact.OfficialEmail, storedEmployee.Email);
        Assert.Equal(storedContact.OfficialPhone, storedEmployee.Phone);
    }

    [Fact]
    public async Task Contact_rejects_an_official_email_used_by_another_employee()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        var employeeId = OrganizationTestHarness.EmployeeId(Demo01, "EMP-003");

        var result = await harness.Contacts().UpsertAsync(employeeId, new EmployeeContactRequest
        {
            OfficialEmail = "NADIA.FARRELL@DEMO01.COM",
            OfficialPhone = "555-3000"
        });

        Assert.Equal(ResultStatus.Conflict, result.Status);
        Assert.Equal("officialEmail", Assert.Single(result.Errors!).Field);

        using var context = harness.CreateContext();
        Assert.False(await context.EmployeeContacts.AnyAsync(c => c.EmployeeId == employeeId));
        Assert.Equal(
            "priya.raman@demo01.com",
            await context.Employees.Where(e => e.Id == employeeId).Select(e => e.Email).SingleAsync());
    }

    [Fact]
    public async Task Contact_cannot_read_or_update_another_tenants_employee()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        var otherTenantEmployee = OrganizationTestHarness.EmployeeId(Demo02, "E-100");

        var read = await harness.ActAs(Demo01).Contacts().GetAsync(otherTenantEmployee);
        var write = await harness.Contacts().UpsertAsync(otherTenantEmployee, new EmployeeContactRequest
        {
            OfficialEmail = "cross-tenant@example.com"
        });

        Assert.Equal(ResultStatus.NotFound, read.Status);
        Assert.Equal(ResultStatus.NotFound, write.Status);

        using var unscoped = harness.CreateUnscopedContext();
        Assert.False(await unscoped.EmployeeContacts.IgnoreQueryFilters()
            .AnyAsync(c => c.EmployeeId == otherTenantEmployee));
    }

    [Fact]
    public async Task Legacy_employee_create_and_edit_keep_the_contact_record_synchronized()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        var createRequest = LegacyRequest("EMP-CONTACT", "legacy.contact@demo01.com", "555-4000");

        var created = await harness.Employees().CreateAsync(createRequest);
        var contactAfterCreate = await harness.Contacts().GetAsync(created.Value!.Id);

        Assert.True(created.Succeeded);
        Assert.Equal(createRequest.Email, contactAfterCreate.Value!.OfficialEmail);
        Assert.Equal(createRequest.Phone, contactAfterCreate.Value.OfficialPhone);

        var updateRequest = LegacyRequest("EMP-CONTACT", "legacy.updated@demo01.com", "555-4001");
        var updated = await harness.Employees().UpdateAsync(created.Value.Id, updateRequest);
        var contactAfterUpdate = await harness.Contacts().GetAsync(created.Value.Id);

        Assert.True(updated.Succeeded);
        Assert.Equal(updateRequest.Email, contactAfterUpdate.Value!.OfficialEmail);
        Assert.Equal(updateRequest.Phone, contactAfterUpdate.Value.OfficialPhone);
        Assert.Equal(contactAfterUpdate.Value.OfficialEmail, updated.Value!.Email);
        Assert.Equal(contactAfterUpdate.Value.OfficialPhone, updated.Value.Phone);
    }

    private static EmployeeRequest LegacyRequest(string employeeCode, string email, string phone) => new()
    {
        EmployeeCode = employeeCode,
        FirstName = "Contact",
        LastName = "Regression",
        Email = email,
        Phone = phone,
        DateOfJoining = new DateOnly(2026, 8, 31),
        Status = EmployeeStatus.Active
    };
}
