using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.Services;
using HRMS.Domain.Authorization;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class MyEmployeeProfileServiceTests
{
    private static readonly Guid Tenant = SeedData.TenantIds.Demo01;
    private static readonly Guid OtherTenant = SeedData.TenantIds.Demo02;
    private static readonly Guid User = SeedData.Users[1].Id;
    private static readonly Guid Employee = OrganizationTestHarness.EmployeeId(Tenant, "EMP-001");
    private static readonly Guid Manager = OrganizationTestHarness.EmployeeId(Tenant, "EMP-002");

    [Fact]
    public async Task Linked_account_returns_own_mapped_profile_with_masked_active_bank_details()
    {
        using var harness = await ArrangeAsync();
        var result = await CreateService(harness).GetAsync();

        Assert.True(result.Succeeded, result.Message);
        var profile = result.Value!;
        Assert.Equal("EMP-001", profile.EmployeeCode);
        Assert.Equal("Priya", profile.FirstName);
        Assert.Equal("Raman", profile.LastName);
        Assert.Equal("Priya Raman", profile.FullName);
        Assert.Equal("priya.official@test.local", profile.Contact.Email);
        Assert.Equal("+91-9000000001", profile.Contact.Phone);
        Assert.Equal("Current Street", profile.CurrentAddress!.AddressLine1);
        Assert.Equal("Permanent Street", profile.PermanentAddress!.AddressLine1);
        Assert.Equal("Engineering", profile.CurrentEmployment!.Department);
        Assert.Equal("Engineering Manager", profile.CurrentEmployment.Designation);
        Assert.Equal("Manager Person", profile.CurrentEmployment.ReportingManager);
        Assert.Single(profile.BankDetails);
        Assert.Equal("********7890", profile.BankDetails[0].MaskedAccountNumber);
        Assert.Equal("HDFC*****90", profile.BankDetails[0].MaskedIfsc);
        Assert.Equal("XXXX-XXXX-3333", profile.MaskedAadhaar);
        Assert.Equal("A****F", profile.MaskedPan);
        Assert.Equal("******7777", profile.MaskedUan);
        Assert.Equal("******8888", profile.MaskedPf);
        Assert.Equal("******9999", profile.MaskedEsic);
        Assert.Equal("******0000", profile.MediclaimNumber);

        var serialized = System.Text.Json.JsonSerializer.Serialize(profile);
        foreach (var raw in new[] { "111122223333", "ABCDE1234F", "999988887777", "PF-RAW-8888", "ESIC-RAW-9999", "MED-RAW-0000", "ACC-RAW-7890", "HDFC000090" })
            Assert.DoesNotContain(raw, serialized, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_or_tenant_mismatched_link_returns_not_found_without_email_fallback()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        harness.TenantContext.UserId = User;
        var missing = await CreateService(harness).GetAsync();
        Assert.Equal(ResultStatus.NotFound, missing.Status);
        Assert.Contains("not linked", missing.Message, StringComparison.OrdinalIgnoreCase);

        using (var context = harness.CreateUnscopedContext())
        {
            context.AccountEmployeeCurrentLinks.Add(new AccountEmployeeCurrentLink
            {
                LinkId = Guid.NewGuid(), TenantId = OtherTenant, UserId = User, EmployeeId = Employee
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }

        Assert.Equal(ResultStatus.NotFound, (await CreateService(harness).GetAsync()).Status);
    }

    [Fact]
    public async Task Employee_id_is_not_assumed_equal_to_user_id_and_cross_tenant_employee_is_rejected()
    {
        using var harness = await ArrangeAsync();
        Assert.NotEqual(User, Employee);

        var crossTenant = new MyEmployeeProfileService(
            harness.CreateContext(),
            new FixedIdentity(Result<RuntimeEmployeeIdentity>.Success(new(Tenant, User, OrganizationTestHarness.EmployeeId(OtherTenant, "E-100")))),
            new EffectiveEmploymentResolver(harness.CreateContext(), harness.TenantContext),
            harness.Clock);

        Assert.Equal(ResultStatus.NotFound, (await crossTenant.GetAsync()).Status);
    }

    [Fact]
    public void Employee_role_does_not_receive_broad_employee_view_permission()
    {
        Assert.DoesNotContain(Permissions.Employee.View, SeedData.RolePermissionMap[RoleNames.Employee]);
    }

    private static async Task<OrganizationTestHarness> ArrangeAsync()
    {
        var harness = await OrganizationTestHarness.CreateAsync();
        harness.TenantContext.UserId = User;
        using var context = harness.CreateContext();
        var employee = await context.Employees.SingleAsync(x => x.Id == Employee);
        employee.FirstName = "Priya";
        employee.LastName = "Raman";
        employee.AadhaarNumber = "111122223333";
        employee.PanNumber = "ABCDE1234F";
        employee.UanNumber = "999988887777";
        employee.PfNumber = "PF-RAW-8888";
        employee.EsicApplicable = true;
        employee.EsicNumber = "ESIC-RAW-9999";
        employee.MediclaimNumber = "MED-RAW-0000";
        context.EmployeeContacts.Add(new EmployeeContact { Id = Guid.NewGuid(), TenantId = Tenant, EmployeeId = Employee, OfficialEmail = "priya.official@test.local", OfficialPhone = "+91-9000000001" });
        context.EmployeeAddresses.AddRange(
            new EmployeeAddress { Id = Guid.NewGuid(), TenantId = Tenant, EmployeeId = Employee, AddressType = AddressType.Current, AddressLine1 = "Current Street", City = "Mumbai" },
            new EmployeeAddress { Id = Guid.NewGuid(), TenantId = Tenant, EmployeeId = Employee, AddressType = AddressType.Permanent, AddressLine1 = "Permanent Street", City = "Pune" });
        context.EmployeeEmploymentHistory.Add(new EmployeeEmploymentHistory { Id = Guid.NewGuid(), TenantId = Tenant, EmployeeId = Employee, EffectiveFrom = new(2020, 1, 1), DepartmentId = OrganizationTestHarness.DepartmentId(Tenant, "ENG"), DesignationId = OrganizationTestHarness.DesignationId(Tenant, "EM"), ManagerId = Manager, ManagerName = "Manager Person", EmploymentType = EmploymentType.FullTime, EmploymentStatus = EmployeeStatus.Active });
        context.EmployeeBankDetails.AddRange(
            new EmployeeBankDetail { Id = Guid.NewGuid(), TenantId = Tenant, EmployeeId = Employee, BankId = OrganizationTestHarness.BankId(Tenant, "HDFC"), AccountNumber = "ACC-RAW-7890", IfscCode = "HDFC000090", AccountType = AccountType.Salary, Status = BankAccountStatus.Active, IsActive = true },
            new EmployeeBankDetail { Id = Guid.NewGuid(), TenantId = Tenant, EmployeeId = Employee, BankId = OrganizationTestHarness.BankId(Tenant, "SBI"), AccountNumber = "ACC-INACTIVE", IfscCode = "SBIN000001", AccountType = AccountType.Savings, Status = BankAccountStatus.Closed, IsActive = false });
        var linkId = Guid.NewGuid();
        context.AccountEmployeeLinkEvents.Add(new AccountEmployeeLinkEvent { Id = linkId, TenantId = Tenant, SubjectUserId = User, ActorUserId = User, Sequence = 1, Operation = "Link", NewLinkId = linkId, AfterEmployeeId = Employee, OccurredAtUtc = DateTime.UtcNow, Reason = "profile test", CorrelationId = Guid.NewGuid().ToString("N") });
        context.AccountEmployeeCurrentLinks.Add(new AccountEmployeeCurrentLink { LinkId = linkId, TenantId = Tenant, UserId = User, EmployeeId = Employee });
        await context.SaveChangesAsync();
        return harness;
    }

    private static MyEmployeeProfileService CreateService(OrganizationTestHarness harness)
    {
        var context = harness.CreateContext();
        return new MyEmployeeProfileService(context, new EmployeeIdentityResolver(context, harness.TenantContext), new EffectiveEmploymentResolver(context, harness.TenantContext), harness.Clock);
    }

    private sealed class FixedIdentity(Result<RuntimeEmployeeIdentity> result) : IEmployeeIdentityResolver
    {
        public Task<Result<RuntimeEmployeeIdentity>> ResolveCurrentAsync(CancellationToken cancellationToken = default) => Task.FromResult(result);
    }
}
