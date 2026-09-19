using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class SqlServerSalaryStructureIntegrationTests : IClassFixture<SqlServerIntegrationTestHarness>
{
    private readonly SqlServerIntegrationTestHarness fixture;
    public SqlServerSalaryStructureIntegrationTests(SqlServerIntegrationTestHarness fixture) => this.fixture = fixture;

    [SqlServerSalaryStructureFact, Trait("Category", "SqlServerIntegration")]
    public async Task SqlServer_salary_structure_master_preserves_provider_neutral_semantics()
    {
        var tenantA = Guid.NewGuid(); var tenantB = Guid.NewGuid();
        try
        {
            var basic = Guid.NewGuid(); var hra = Guid.NewGuid();
            await using (var setup = fixture.CreateContext(new TestTenantContext()))
            {
                setup.Tenants.AddRange(Tenant(tenantA, "STRUCTA"), Tenant(tenantB, "STRUCTB"));
                setup.SalaryComponents.AddRange(Component(tenantA, basic, "BASIC"), Component(tenantA, hra, "HRA"), Component(tenantB, Guid.NewGuid(), "BASIC"));
                await setup.SaveChangesAsync();
            }
            await using var dbA = fixture.CreateContext(new TestTenantContext(tenantA));
            await using var dbB = fixture.CreateContext(new TestTenantContext(tenantB));
            var serviceA = new SalaryStructureService(dbA, new TestTenantContext(tenantA), TimeProvider.System);
            var serviceB = new SalaryStructureService(dbB, new TestTenantContext(tenantB), TimeProvider.System);
            var created = await serviceA.CreateAsync(Request("STAFF_MONTHLY", "Staff Monthly", basic, hra));
            Assert.True(created.Succeeded, created.Message);
            Assert.Equal(ResultStatus.Conflict, (await serviceA.CreateAsync(Request("STAFF_MONTHLY", "Duplicate", basic))).Status);
            Assert.True((await serviceB.CreateAsync(Request("STAFF_MONTHLY", "Staff Monthly", await ComponentIdAsync(dbB, "BASIC")))).Succeeded);
            var invalid = Request("INVALID", "Invalid", basic); invalid.Components[0].Value = 101; invalid.Components[0].CalculationType = SalaryStructureCalculationType.Percentage; invalid.Components[0].PercentageOfComponentId = hra;
            Assert.Equal(ResultStatus.ValidationFailed, (await serviceA.CreateAsync(invalid)).Status);
            var page = await serviceA.GetAsync(new SalaryStructureQuery { Page = 1, PageSize = 1, Search = "staff" });
            Assert.Equal(1, page.Value!.Items.Count); Assert.Equal("STAFF_MONTHLY", page.Value.Items[0].Code);
            var update = Request("STAFF_MONTHLY", "Staff Monthly Updated", basic, hra); update.ExpectedConcurrencyVersion = created.Value!.ConcurrencyVersion;
            var updated = await serviceA.UpdateAsync(created.Value.Id, update); Assert.True(updated.Succeeded, updated.Message);
            var deactivated = await serviceA.SetActiveAsync(created.Value.Id, false, updated.Value!.ConcurrencyVersion); Assert.True(deactivated.Succeeded);
            Assert.True((await serviceA.GetHistoryAsync(created.Value.Id)).Value!.Count >= 3);
            Assert.Equal(ResultStatus.NotFound, (await serviceB.GetByIdAsync(created.Value.Id)).Status);
        }
        finally
        {
            await using var cleanup = fixture.CreateContext(new TestTenantContext());
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [SalaryStructureHistories] WHERE [TenantId] IN ({tenantA}, {tenantB})");
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [SalaryStructureComponents] WHERE [TenantId] IN ({tenantA}, {tenantB})");
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [SalaryStructureVersions] WHERE [TenantId] IN ({tenantA}, {tenantB})");
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [SalaryStructures] WHERE [TenantId] IN ({tenantA}, {tenantB})");
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [SalaryComponents] WHERE [TenantId] IN ({tenantA}, {tenantB})");
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [Tenants] WHERE [Id] IN ({tenantA}, {tenantB})");
        }
    }

    private static SalaryStructureRequest Request(string code, string name, Guid basic, Guid? hra = null) => new()
    {
        Code = code, Name = name, EffectiveFrom = new DateOnly(2026, 1, 1), IsActive = true,
        Components = Rows(basic, hra)
    };
    private static List<SalaryStructureComponentRequest> Rows(Guid basic, Guid? hra) { var rows = new List<SalaryStructureComponentRequest> { new() { SalaryComponentId = basic, Sequence = 1, CalculationType = SalaryStructureCalculationType.FixedAmount, Value = 10000, IsProratable = true } }; if (hra is Guid id) rows.Add(new() { SalaryComponentId = id, Sequence = 2, CalculationType = SalaryStructureCalculationType.Percentage, Value = 40, PercentageOfComponentId = basic, IsProratable = true }); return rows; }
    private static SalaryComponent Component(Guid tenant, Guid id, string code) => new() { Id = id, TenantId = tenant, Code = code, Name = code, ComponentType = SalaryComponentType.Earning, CalculationType = SalaryCalculationType.FixedAmount, EffectiveFrom = new DateOnly(2026, 1, 1), IsActive = true };
    private static async Task<Guid> ComponentIdAsync(HrmsDbContext db, string code) => await db.SalaryComponents.Where(x => x.Code == code).Select(x => x.Id).SingleAsync();
    private static Tenant Tenant(Guid id, string code) => new() { Id = id, TenantCode = code, TenantName = code, Host = $"{code.ToLowerInvariant()}.test", ShardKey = $"{code.ToLowerInvariant()}-shard", Status = TenantStatus.Active, DatabaseProvider = DatabaseProviderType.SqlServer };
}

public sealed class SqlServerSalaryStructureFactAttribute : FactAttribute
{
    public SqlServerSalaryStructureFactAttribute() => Skip = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SqlServerIntegrationTestHarness.RequiredEnvironmentVariable))
        ? $"SQL Server Salary Structure tests not executed: {SqlServerIntegrationTestHarness.RequiredEnvironmentVariable} is absent."
        : null;
}
