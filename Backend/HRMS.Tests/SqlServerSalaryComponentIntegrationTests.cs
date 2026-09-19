using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class SqlServerSalaryComponentIntegrationTests : IClassFixture<SqlServerIntegrationTestHarness>
{
    private readonly SqlServerIntegrationTestHarness fixture;

    public SqlServerSalaryComponentIntegrationTests(SqlServerIntegrationTestHarness fixture) => this.fixture = fixture;

    [Fact, Trait("Category", "SqlServerIntegration")]
    public async Task SqlServer_salary_component_master_preserves_provider_neutral_semantics()
    {
        if (!fixture.IsConfigured) return;

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        try
        {
            await using (var setup = fixture.CreateContext(new TestTenantContext()))
            {
                setup.Tenants.AddRange(Tenant(tenantA, "SQLA"), Tenant(tenantB, "SQLB"));
                await setup.SaveChangesAsync();
            }

            await using var dbA = fixture.CreateContext(new TestTenantContext(tenantA));
            await using var dbB = fixture.CreateContext(new TestTenantContext(tenantB));
            var serviceA = new SalaryComponentService(dbA, new TestTenantContext(tenantA), TimeProvider.System);
            var serviceB = new SalaryComponentService(dbB, new TestTenantContext(tenantB), TimeProvider.System);

            var basic = await serviceA.CreateAsync(Request("BASIC", "Basic Salary", SalaryComponentType.Earning, SalaryCalculationType.FixedAmount, taxable: true));
            Assert.True(basic.Succeeded, basic.Message);
            Assert.Equal(ResultStatus.Conflict, (await serviceA.CreateAsync(Request("BASIC", "Duplicate"))).Status);
            var otherBasic = await serviceB.CreateAsync(Request("BASIC", "Basic Salary"));
            Assert.True(otherBasic.Succeeded, otherBasic.Message);

            var hra = await serviceA.CreateAsync(Request("HRA", "House Rent Allowance", SalaryComponentType.Earning, SalaryCalculationType.Percentage, taxable: true));
            var pf = await serviceA.CreateAsync(Request("PF_EMPLOYEE", "Provident Fund - Employee", SalaryComponentType.Deduction, SalaryCalculationType.Statutory, statutory: true));
            Assert.True(hra.Succeeded && pf.Succeeded);

            var page = await serviceA.GetAsync(new SalaryComponentQuery { Page = 1, PageSize = 2 });
            Assert.Equal(3, page.Value!.TotalCount);
            Assert.Equal(2, page.Value.Items.Count);
            var filtered = await serviceA.GetAsync(new SalaryComponentQuery { Search = "basic", ComponentType = SalaryComponentType.Earning, CalculationType = SalaryCalculationType.FixedAmount, IsActive = true, IsStatutory = false });
            Assert.Single(filtered.Value!.Items);

            var update = Request("HRA", "House Rent Allowance Updated", SalaryComponentType.Earning, SalaryCalculationType.Percentage, taxable: true);
            update.ExpectedConcurrencyVersion = hra.Value!.ConcurrencyVersion;
            var updated = await serviceA.UpdateAsync(hra.Value.Id, update);
            Assert.True(updated.Succeeded, updated.Message);
            var deactivated = await serviceA.SetActiveAsync(hra.Value.Id, false, updated.Value!.ConcurrencyVersion);
            Assert.True(deactivated.Succeeded);
            var activated = await serviceA.SetActiveAsync(hra.Value.Id, true, deactivated.Value!.ConcurrencyVersion);
            Assert.True(activated.Succeeded);
            Assert.True((await serviceA.GetHistoryAsync(hra.Value.Id)).Value!.Count >= 4);

            var invalidStatutory = Request("INVALID", "Invalid", SalaryComponentType.Deduction, SalaryCalculationType.Statutory);
            invalidStatutory.StatutoryType = SalaryStatutoryType.ProvidentFund;
            Assert.Equal(ResultStatus.ValidationFailed, (await serviceA.CreateAsync(invalidStatutory)).Status);
            var invalidDates = Request("DATES", "Dates");
            invalidDates.EffectiveFrom = new DateOnly(2027, 1, 1);
            invalidDates.EffectiveTo = new DateOnly(2026, 12, 31);
            Assert.Equal(ResultStatus.ValidationFailed, (await serviceA.CreateAsync(invalidDates)).Status);

            Assert.Equal(ResultStatus.NotFound, (await serviceA.GetByIdAsync(otherBasic.Value!.Id)).Status);
            Assert.Equal(ResultStatus.NotFound, (await serviceA.UpdateAsync(otherBasic.Value.Id, Request("OTHER", "Other"))).Status);
            Assert.Equal(ResultStatus.NotFound, (await serviceA.SetActiveAsync(otherBasic.Value.Id, false, null)).Status);
            Assert.Equal(ResultStatus.NotFound, (await serviceA.GetHistoryAsync(otherBasic.Value.Id)).Status);
        }
        finally
        {
            await using var cleanup = fixture.CreateContext(new TestTenantContext());
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [SalaryComponentHistories] WHERE [TenantId] IN ({tenantA}, {tenantB})");
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [SalaryComponents] WHERE [TenantId] IN ({tenantA}, {tenantB})");
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [Tenants] WHERE [Id] IN ({tenantA}, {tenantB})");
        }
    }

    private static Tenant Tenant(Guid id, string code) => new() { Id = id, TenantCode = code, TenantName = code, Host = $"{code.ToLowerInvariant()}.test", ShardKey = $"{code.ToLowerInvariant()}-shard", Status = TenantStatus.Active, DatabaseProvider = DatabaseProviderType.SqlServer };

    private static SalaryComponentRequest Request(string code, string name, SalaryComponentType type = SalaryComponentType.Earning, SalaryCalculationType calculation = SalaryCalculationType.FixedAmount, bool taxable = false, bool statutory = false) => new()
    {
        Code = code,
        Name = name,
        ComponentType = type,
        CalculationType = calculation,
        IsTaxable = taxable,
        IsStatutory = statutory,
        StatutoryType = statutory ? SalaryStatutoryType.ProvidentFund : SalaryStatutoryType.None,
        IsRecurring = true,
        AffectsGross = type == SalaryComponentType.Earning,
        AffectsNetPay = type != SalaryComponentType.EmployerContribution,
        EffectiveFrom = new DateOnly(2026, 1, 1),
        IsActive = true,
    };
}
