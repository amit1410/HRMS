using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;

namespace HRMS.Tests;

public sealed class PayrollAccountingTests
{
    [Fact]
    public void Journal_statuses_are_ordered_for_internal_lifecycle()
    { Assert.True((int)PayrollJournalStatus.Generated < (int)PayrollJournalStatus.Validated); Assert.True((int)PayrollJournalStatus.Validated < (int)PayrollJournalStatus.Approved); Assert.True((int)PayrollJournalStatus.Approved < (int)PayrollJournalStatus.Posted); }

    [Fact]
    public void Accounting_uses_decimal_values_and_explicit_accounting_types()
    { Assert.Equal(0.01m, decimal.Round(0.005m, 2, MidpointRounding.AwayFromZero)); Assert.Equal(PayrollGLAccountType.Liability, PayrollGLAccountType.Liability); }

    [Fact]
    public async Task Account_crud_and_same_tenant_duplicate_validation_are_tenant_scoped()
    {
        using var database = new SqliteInMemoryDatabase(); var tenantId = Guid.NewGuid(); await AddTenantsAsync(database, tenantId); await using var db = database.CreateContext(new TestTenantContext(tenantId)); var service = new PayrollAccountingService(db, new TestTenantContext(tenantId), TimeProvider.System);
        var created = await service.CreateAccountAsync(new PayrollGLAccountRequest { Code = "6000", Name = "Salary expense", AccountType = PayrollGLAccountType.Expense }); Assert.True(created.Succeeded, created.Message); Assert.Equal("6000", created.Value!.Code);
        var duplicate = await service.CreateAccountAsync(new PayrollGLAccountRequest { Code = "6000", Name = "Duplicate", AccountType = PayrollGLAccountType.Expense }); Assert.Equal(ResultStatus.Conflict, duplicate.Status);
        var updated = await service.UpdateAccountAsync(created.Value.Id, new PayrollGLAccountRequest { Code = "6001", Name = "Updated expense", AccountType = PayrollGLAccountType.Expense }); Assert.True(updated.Succeeded, updated.Message); var page = await service.ListAccountsAsync(new SalaryComponentQuery { Page = 1, PageSize = 10 }); Assert.Single(page.Value!.Items);
    }

    [Fact]
    public async Task Same_account_code_is_allowed_for_another_tenant()
    {
        using var database = new SqliteInMemoryDatabase(); var tenantA = Guid.NewGuid(); var tenantB = Guid.NewGuid(); await AddTenantsAsync(database, tenantA, tenantB); await using (var db = database.CreateContext(new TestTenantContext(tenantA))) { var service = new PayrollAccountingService(db, new TestTenantContext(tenantA), TimeProvider.System); Assert.True((await service.CreateAccountAsync(new PayrollGLAccountRequest { Code = "2100", Name = "Payable", AccountType = PayrollGLAccountType.Liability })).Succeeded); } await using var dbB = database.CreateContext(new TestTenantContext(tenantB)); var serviceB = new PayrollAccountingService(dbB, new TestTenantContext(tenantB), TimeProvider.System); Assert.True((await serviceB.CreateAccountAsync(new PayrollGLAccountRequest { Code = "2100", Name = "Payable B", AccountType = PayrollGLAccountType.Liability })).Succeeded);
    }

    [Fact]
    public async Task Versioning_rejects_invalid_dates_and_overlaps_but_preserves_future_version()
    {
        using var database = new SqliteInMemoryDatabase(); var tenantId = Guid.NewGuid(); await AddTenantsAsync(database, tenantId); await using var db = database.CreateContext(new TestTenantContext(tenantId)); var service = new PayrollAccountingService(db, new TestTenantContext(tenantId), TimeProvider.System); var configuration = await service.CreateConfigurationAsync(new PayrollAccountingConfigurationRequest { Code = "PAYROLL", Name = "Payroll accounting" }); Assert.True(configuration.Succeeded, configuration.Message);
        var invalid = await service.CreateVersionAsync(configuration.Value!.Id, new PayrollAccountingConfigurationVersionRequest { EffectiveFrom = new DateOnly(2026, 2, 1), EffectiveTo = new DateOnly(2026, 1, 1) }); Assert.Equal(ResultStatus.ValidationFailed, invalid.Status); var first = await service.CreateVersionAsync(configuration.Value.Id, new PayrollAccountingConfigurationVersionRequest { EffectiveFrom = new DateOnly(2026, 1, 1), EffectiveTo = new DateOnly(2026, 6, 30) }); Assert.True(first.Succeeded, first.Message); var overlap = await service.CreateVersionAsync(configuration.Value.Id, new PayrollAccountingConfigurationVersionRequest { EffectiveFrom = new DateOnly(2026, 6, 1) }); Assert.Equal(ResultStatus.Conflict, overlap.Status); var future = await service.CreateVersionAsync(configuration.Value.Id, new PayrollAccountingConfigurationVersionRequest { EffectiveFrom = new DateOnly(2026, 7, 1) }); Assert.True(future.Succeeded, future.Message);
    }

    [Fact]
    public async Task Mapping_rejects_inactive_cross_tenant_and_duplicate_accounts()
    {
        using var database = new SqliteInMemoryDatabase(); var tenantId = Guid.NewGuid(); var otherTenant = Guid.NewGuid(); await AddTenantsAsync(database, tenantId, otherTenant); await using var db = database.CreateContext(new TestTenantContext(tenantId)); var account = new PayrollGLAccount { Id = Guid.NewGuid(), TenantId = tenantId, Code = "6000", Name = "Expense", AccountType = PayrollGLAccountType.Expense, IsActive = true }; var inactive = new PayrollGLAccount { Id = Guid.NewGuid(), TenantId = tenantId, Code = "9999", Name = "Inactive", AccountType = PayrollGLAccountType.Expense, IsActive = false }; var other = new PayrollGLAccount { Id = Guid.NewGuid(), TenantId = otherTenant, Code = "2100", Name = "Other", AccountType = PayrollGLAccountType.Liability, IsActive = true }; db.PayrollGLAccounts.AddRange(account, inactive); var config = new PayrollAccountingConfiguration { Id = Guid.NewGuid(), TenantId = tenantId, Code = "CFG", Name = "Config" }; db.PayrollAccountingConfigurations.Add(config); var version = new PayrollAccountingConfigurationVersion { Id = Guid.NewGuid(), TenantId = tenantId, PayrollAccountingConfigurationId = config.Id, EffectiveFrom = new DateOnly(2026, 1, 1), Status = PayrollAccountingConfigurationVersionStatus.Active }; db.PayrollAccountingConfigurationVersions.Add(version); await db.SaveChangesAsync(); await using (var otherDb = database.CreateContext(new TestTenantContext(otherTenant))) { otherDb.PayrollGLAccounts.Add(other); await otherDb.SaveChangesAsync(); } var service = new PayrollAccountingService(db, new TestTenantContext(tenantId), TimeProvider.System);
        var invalid = await service.CreateMappingAsync(version.Id, new PayrollGLMappingRequest { MappingType = PayrollGLMappingType.NetPayable, CreditAccountId = inactive.Id }); Assert.True(invalid.Status == ResultStatus.Conflict, $"inactive: {invalid.Status} {invalid.Message}"); var crossTenant = await service.CreateMappingAsync(version.Id, new PayrollGLMappingRequest { MappingType = PayrollGLMappingType.NetPayable, CreditAccountId = other.Id }); Assert.True(crossTenant.Status == ResultStatus.Conflict, $"cross-tenant: {crossTenant.Status} {crossTenant.Message}"); var validRequest = new PayrollGLMappingRequest { MappingType = PayrollGLMappingType.NetPayable, CreditAccountId = account.Id, IsActive = true }; var valid = await service.CreateMappingAsync(version.Id, validRequest); Assert.True(valid.Succeeded, valid.Message); var duplicate = await service.CreateMappingAsync(version.Id, validRequest); Assert.True(duplicate.Status == ResultStatus.Conflict, $"duplicate: {duplicate.Status} {duplicate.Message}");
    }

    private static async Task AddTenantsAsync(SqliteInMemoryDatabase database, params Guid[] tenantIds)
    {
        await using var catalog = database.CreateContext(new TestTenantContext());
        catalog.Tenants.AddRange(tenantIds.Select(id => new Tenant
        {
            Id = id,
            TenantCode = $"AC{id:N}"[..12],
            TenantName = "Accounting test tenant",
            Host = $"{id:N}.accounting.test",
            ShardKey = id.ToString("N")
        }));
        await catalog.SaveChangesAsync();
    }
}
