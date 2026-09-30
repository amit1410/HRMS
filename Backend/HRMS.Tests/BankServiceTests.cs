using HRMS.Application.DTOs.Banks;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

/// <summary>
/// Bank master behaviour, exercised through the real service over a real (SQLite) database so the tenant
/// query filter and the unique indexes participate rather than being mocked away.
/// </summary>
public class BankServiceTests
{
    private static readonly Guid Demo01 = SeedData.TenantIds.Demo01;
    private static readonly Guid Demo02 = SeedData.TenantIds.Demo02;

    [Fact]
    public async Task Get_returns_only_the_callers_own_banks()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var mine = await harness.ActAs(Demo01).Banks().GetAsync(new BankQuery());
        var theirs = await harness.ActAs(Demo02).Banks().GetAsync(new BankQuery());

        Assert.Equal(new[] { "AXIS", "HDFC", "ICICI", "SBI" }, mine.Value!.Items.Select(b => b.Code));
        Assert.Equal(new[] { "BOB", "PNB" }, theirs.Value!.Items.Select(b => b.Code));
    }

    [Fact]
    public async Task Get_filters_by_active_state()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var active = await harness.Banks().GetAsync(new BankQuery { IsActive = true });
        var inactive = await harness.Banks().GetAsync(new BankQuery { IsActive = false });

        Assert.Equal(new[] { "HDFC", "ICICI", "SBI" }, active.Value!.Items.Select(b => b.Code));
        Assert.Equal(new[] { "AXIS" }, inactive.Value!.Items.Select(b => b.Code));
    }

    [Fact]
    public async Task Search_matches_code_and_name_regardless_of_case()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var byCode = await harness.Banks().GetAsync(new BankQuery { Search = "sbi" });
        var byName = await harness.Banks().GetAsync(new BankQuery { Search = "HDFC BANK" });

        Assert.Equal(new[] { "SBI" }, byCode.Value!.Items.Select(b => b.Code));
        Assert.Equal(new[] { "HDFC" }, byName.Value!.Items.Select(b => b.Code));
    }

    [Fact]
    public async Task Create_persists_all_fields_and_normalizes_ifsc_prefix()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var created = await harness.Banks().CreateAsync(new BankRequest
        {
            Code = "KOTAK",
            Name = "Kotak Mahindra Bank",
            ShortName = "Kotak",
            IfscPrefix = "kkbk",
            BankType = BankType.Private,
            Country = "India",
            EffectiveFrom = new DateOnly(2024, 4, 1),
            Remarks = "Onboarded for payroll.",
            IsActive = true,
        });

        Assert.True(created.Succeeded);
        Assert.Equal("KKBK", created.Value!.IfscPrefix);
        Assert.Equal(BankType.Private, created.Value.BankType);
        Assert.Equal(new DateOnly(2024, 4, 1), created.Value.EffectiveFrom);
    }

    [Fact]
    public async Task Create_rejects_a_duplicate_code_case_insensitively()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var result = await harness.Banks().CreateAsync(new BankRequest { Code = "sbi", Name = "Another Bank", IsActive = true });

        Assert.False(result.Succeeded);
        Assert.Contains("code", result.Errors!.Select(e => e.Field));
    }

    [Fact]
    public async Task Duplicate_code_in_one_tenant_does_not_block_another_tenant()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var result = await harness.ActAs(Demo02).Banks().CreateAsync(new BankRequest { Code = "SBI", Name = "State Bank", IsActive = true });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Deactivate_then_activate_flips_the_flag_without_deleting()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        var id = OrganizationTestHarness.BankId(Demo01, "SBI");

        var deactivated = await harness.Banks().SetActiveAsync(id, isActive: false);
        var reactivated = await harness.Banks().SetActiveAsync(id, isActive: true);

        Assert.False(deactivated.Value!.IsActive);
        Assert.True(reactivated.Value!.IsActive);
    }

    [Fact]
    public async Task Delete_removes_an_unreferenced_bank()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        var created = await harness.Banks().CreateAsync(new BankRequest { Code = "TEMP", Name = "Temporary Bank", IsActive = true });

        var deleted = await harness.Banks().DeleteAsync(created.Value!.Id);

        Assert.True(deleted.Succeeded);
        var after = await harness.Banks().GetByIdAsync(created.Value.Id);
        Assert.False(after.Succeeded);
    }

    [Fact]
    public async Task Delete_is_refused_when_an_employee_bank_record_references_the_bank()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        var bankId = OrganizationTestHarness.BankId(Demo01, "SBI");

        await using (var context = harness.CreateContext())
        {
            var employee = await context.Employees.FirstAsync(e => e.TenantId == Demo01);
            context.EmployeeBankDetails.Add(new EmployeeBankDetail
            {
                Id = Guid.NewGuid(),
                TenantId = Demo01,
                EmployeeId = employee.Id,
                BankId = bankId,
                AccountHolderName = "Test Holder",
                AccountNumber = "000111222333",
                AccountType = AccountType.Savings,
                AccountPurpose = AccountPurpose.Salary,
                IsActive = true,
            });
            await context.SaveChangesAsync();
        }

        var result = await harness.Banks().DeleteAsync(bankId);

        Assert.False(result.Succeeded);
        Assert.Equal(HRMS.Application.Common.ResultStatus.Conflict, result.Status);
    }
}
