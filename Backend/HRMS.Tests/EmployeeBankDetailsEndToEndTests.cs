using HRMS.Application.Common;
using HRMS.Application.DTOs.Employees;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

/// <summary>
/// End-to-end proof of the complete bank-details flow: bank master → dropdown → add → save → database →
/// edit → update → database → delete (soft) → deactivate in the database → reload. Each step reads the raw
/// row through an unscoped context so the "what really got stored" part is asserted, not assumed.
///
/// The rules under test are the ones the feature shipped with:
///   • the bank is a foreign key to the tenant-scoped bank master, never free text;
///   • delete is a soft delete — the row stays, only IsActive flips to false (no SQL DELETE);
///   • one active record per AccountPurpose; a second active record for the same purpose is refused;
///   • the bank master is never removed by an employee-bank delete.
/// </summary>
public class EmployeeBankDetailsEndToEndTests
{
    private static readonly Guid Demo01 = SeedData.TenantIds.Demo01;
    private static readonly Guid EmployeeId = OrganizationTestHarness.EmployeeId(Demo01, "EMP-001");
    private static readonly Guid SbiId = OrganizationTestHarness.BankId(Demo01, "SBI");
    private static readonly Guid HdfcId = OrganizationTestHarness.BankId(Demo01, "HDFC");
    private static readonly DateOnly Effective = new(2025, 4, 1);

    [Fact]
    public async Task Add_save_edit_update_soft_delete_and_reload_round_trip()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        // --- Add: the API response reflects every requested field plus the master's bank name. ---
        var created = await harness.BankDetails().CreateAsync(EmployeeId, SalaryRequest(SbiId, "EMP-001 Salary"));
        Assert.True(created.Succeeded, created.Message);
        var dto = created.Value!;
        Assert.Equal(SbiId, dto.BankId);
        Assert.Equal("State Bank of India", dto.BankName);         // denormalized from the bank master
        Assert.Equal("EMP-001 Salary", dto.AccountHolderName);
        Assert.Equal("********-100", dto.MaskedAccountNumber);
        Assert.Equal(AccountType.Savings, dto.AccountType);
        Assert.Equal(AccountPurpose.Salary, dto.AccountPurpose);
        Assert.Equal(BankAccountStatus.Active, dto.Status);
        Assert.Equal("SBIN*****01", dto.MaskedIfscCode);
        Assert.Equal("Main Branch", dto.BranchName);
        Assert.Equal(Effective, dto.EffectiveFrom);
        Assert.True(dto.IsActive);

        var editable = (await harness.BankDetails().GetForEditAsync(EmployeeId, dto.Id)).Value!;
        Assert.Equal("ACC-100", editable.AccountNumber);
        Assert.Equal("SBIN0000001", editable.IfscCode);

        // --- Database: the raw row holds the exact values the form sent, with the FK to the bank master. ---
        AssertStored(await LoadRawAsync(harness, dto.Id), SalaryRequest(SbiId, "EMP-001 Salary"));

        // --- Reload: a fresh read returns the same records, bank name included. ---
        var reloaded = await harness.BankDetails().GetAsync(EmployeeId);
        Assert.True(reloaded.Succeeded);
        var reloadedDto = Assert.Single(reloaded.Value!);
        Assert.Equal(dto.Id, reloadedDto.Id);
        Assert.Equal("State Bank of India", reloadedDto.BankName);
        Assert.True(reloadedDto.IsActive);

        // --- Edit: every field can change, including the bank and purpose. ---
        var changed = SalaryRequest(HdfcId, "EMP-001 Salary Updated");
        changed.AccountNumber = "ACC-200";
        changed.AccountType = AccountType.Current;
        changed.IfscCode = "HDFC0000002";
        changed.BranchName = "Bandra West";
        changed.EffectiveFrom = Effective.AddDays(30);
        changed.Status = BankAccountStatus.Active;

        var updated = await harness.BankDetails().UpdateAsync(EmployeeId, dto.Id, changed);
        Assert.True(updated.Succeeded, updated.Message);
        Assert.Equal(HdfcId, updated.Value!.BankId);
        Assert.Equal("HDFC Bank", updated.Value.BankName);
        Assert.Equal("********-200", updated.Value.MaskedAccountNumber);
        Assert.Equal(BankAccountStatus.Active, updated.Value.Status);
        AssertStored(await LoadRawAsync(harness, dto.Id), changed);

        // --- Delete is a soft delete: the row survives, IsActive flips to false. ---
        var deleted = await harness.BankDetails().DeleteAsync(EmployeeId, dto.Id);
        Assert.True(deleted.Succeeded, deleted.Message);

        var softDeleted = await LoadRawAsync(harness, dto.Id);
        Assert.False(softDeleted.IsActive);
        Assert.Equal(BankAccountStatus.Closed, softDeleted.Status);
        Assert.Equal(HdfcId, softDeleted.BankId);
        Assert.Equal("HDFC Bank", softDeleted.Bank!.Name);
        Assert.Equal(changed.AccountNumber, softDeleted.AccountNumber);

        // --- The bank master was not removed by the employee-bank delete. ---
        var bankAlive = await harness.CreateUnscopedContext().Banks.IgnoreQueryFilters()
            .SingleAsync(b => b.Id == HdfcId);
        Assert.True(bankAlive.IsActive);

        // --- Reload again: the record comes back, now flagged inactive (kept for history). ---
        var reloadedAfterDelete = await harness.BankDetails().GetAsync(EmployeeId);
        Assert.True(reloadedAfterDelete.Succeeded);
        var flagged = Assert.Single(reloadedAfterDelete.Value!);
        Assert.False(flagged.IsActive);
    }

    [Fact]
    public async Task One_active_record_per_purpose_is_enforced()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var first = await harness.BankDetails().CreateAsync(EmployeeId, SalaryRequest(SbiId, "First Salary"));
        Assert.True(first.Succeeded, first.Message);

        // A second active salary account is refused.
        var secondSamePurpose = await harness.BankDetails().CreateAsync(
            EmployeeId, SalaryRequest(HdfcId, "Second Salary"));
        Assert.False(secondSamePurpose.Succeeded);
        Assert.Equal(ResultStatus.Conflict, secondSamePurpose.Status);

        // A different purpose is allowed — the employee may hold one active per purpose.
        var grat = await harness.BankDetails().CreateAsync(EmployeeId, GratuityRequest(HdfcId, "Gratuity Acct", "ACC-G1"));
        Assert.True(grat.Succeeded, grat.Message);

        // Deactivating the first salary account frees the purpose for a new active one.
        await harness.BankDetails().DeleteAsync(EmployeeId, first.Value!.Id);
        var replacement = await harness.BankDetails().CreateAsync(
            EmployeeId, SalaryRequest(HdfcId, "Replacement Salary"));
        Assert.True(replacement.Succeeded, replacement.Message);
    }

    [Fact]
    public async Task Deleted_purpose_can_be_reused_and_bank_master_is_never_deleted()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var salary = await harness.BankDetails().CreateAsync(EmployeeId, SalaryRequest(SbiId, "Main Salary"));
        Assert.True(salary.Succeeded, salary.Message);

        // Delete only deactivates the employee's record — the bank row used to reference remains.
        await harness.BankDetails().DeleteAsync(EmployeeId, salary.Value!.Id);

        var bankCount = await harness.CreateUnscopedContext().Banks.IgnoreQueryFilters()
            .CountAsync(b => b.TenantId == Demo01 && b.Id == SbiId);
        Assert.Equal(1, bankCount);

        var employeeBankCount = await harness.CreateUnscopedContext().EmployeeBankDetails.IgnoreQueryFilters()
            .CountAsync(b => b.TenantId == Demo01 && b.EmployeeId == EmployeeId && b.Id == salary.Value.Id);
        Assert.Equal(1, employeeBankCount);
    }

    [Theory]
    [InlineData(BankAccountStatus.Frozen)]
    [InlineData(BankAccountStatus.Closed)]
    public async Task Non_active_status_moves_the_record_to_immutable_history_and_requires_a_new_replacement(
        BankAccountStatus historicalStatus)
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var current = await harness.BankDetails().CreateAsync(EmployeeId, SalaryRequest(SbiId, "Current Salary"));
        Assert.True(current.Succeeded, current.Message);

        var transition = SalaryRequest(SbiId, "Historical Salary");
        transition.Status = historicalStatus;
        var historical = await harness.BankDetails().UpdateAsync(EmployeeId, current.Value!.Id, transition);

        Assert.True(historical.Succeeded, historical.Message);
        Assert.False(historical.Value!.IsActive);
        Assert.Equal(historicalStatus, historical.Value.Status);

        var sensitiveRead = await harness.BankDetails().GetForEditAsync(EmployeeId, current.Value.Id);
        Assert.Equal(ResultStatus.Conflict, sensitiveRead.Status);
        Assert.Null(sensitiveRead.Value);

        var attemptedReactivation = SalaryRequest(SbiId, "Reactivated Old Salary");
        var reactivation = await harness.BankDetails().UpdateAsync(
            EmployeeId, current.Value.Id, attemptedReactivation);
        Assert.Equal(ResultStatus.Conflict, reactivation.Status);

        var replacement = await harness.BankDetails().CreateAsync(
            EmployeeId, SalaryRequest(HdfcId, "Replacement Salary"));
        Assert.True(replacement.Succeeded, replacement.Message);
        Assert.NotEqual(current.Value.Id, replacement.Value!.Id);

        var listed = (await harness.BankDetails().GetAsync(EmployeeId)).Value!;
        Assert.Equal(2, listed.Count);
        Assert.Equal(replacement.Value.Id, listed[0].Id);
        Assert.True(listed[0].IsActive);
        Assert.Equal(current.Value.Id, listed[1].Id);
        Assert.False(listed[1].IsActive);
        Assert.StartsWith("*", listed[1].MaskedAccountNumber);

        using var unscoped = harness.CreateUnscopedContext();
        Assert.Equal(2, await unscoped.EmployeeBankDetails.IgnoreQueryFilters()
            .CountAsync(b => b.EmployeeId == EmployeeId && b.AccountPurpose == AccountPurpose.Salary));
    }

    [Theory]
    [InlineData(BankAccountStatus.Frozen)]
    [InlineData(BankAccountStatus.Closed)]
    public async Task New_bank_detail_must_start_active(BankAccountStatus status)
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        var request = SalaryRequest(SbiId, "Invalid New Status");
        request.Status = status;

        var result = await harness.BankDetails().CreateAsync(EmployeeId, request);

        Assert.Equal(ResultStatus.ValidationFailed, result.Status);
        Assert.Equal("status", Assert.Single(result.Errors!).Field);
        Assert.Empty((await harness.BankDetails().GetAsync(EmployeeId)).Value!);
    }

    private static EmployeeBankDetailRequest SalaryRequest(Guid bankId, string holder) => new()
    {
        BankId = bankId,
        AccountHolderName = holder,
        AccountNumber = "ACC-100",
        AccountType = AccountType.Savings,
        AccountPurpose = AccountPurpose.Salary,
        Status = BankAccountStatus.Active,
        IfscCode = "SBIN0000001",
        BranchName = "Main Branch",
        EffectiveFrom = Effective
    };

    private static EmployeeBankDetailRequest GratuityRequest(Guid bankId, string holder, string accountNumber) => new()
    {
        BankId = bankId,
        AccountHolderName = holder,
        AccountNumber = accountNumber,
        AccountType = AccountType.Current,
        AccountPurpose = AccountPurpose.Gratuity,
        IfscCode = "HDFC0000002",
        BranchName = "Bandra West"
    };

    private static async Task<EmployeeBankDetail> LoadRawAsync(OrganizationTestHarness harness, Guid id)
    {
        var context = harness.CreateUnscopedContext();
        return await context.EmployeeBankDetails.IgnoreQueryFilters()
            .Include(b => b.Bank)
            .SingleAsync(b => b.Id == id);
    }

    private static void AssertStored(EmployeeBankDetail entity, EmployeeBankDetailRequest request)
    {
        Assert.Equal(request.BankId, entity.BankId);
        Assert.Equal(request.AccountHolderName, entity.AccountHolderName);
        Assert.Equal(request.AccountNumber, entity.AccountNumber);
        Assert.Equal(request.AccountType, entity.AccountType);
        Assert.Equal(request.AccountPurpose, entity.AccountPurpose);
        Assert.Equal(request.Status, entity.Status);
        Assert.Equal(request.IfscCode, entity.IfscCode);
        Assert.Equal(request.BranchName, entity.BranchName);
        Assert.Equal(request.EffectiveFrom, entity.EffectiveFrom);
    }
}
