using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit.Sdk;

namespace HRMS.Tests;

public sealed class SqlServerStatutoryPayrollIntegrationTests : IClassFixture<SqlServerIntegrationTestHarness>
{
    private readonly SqlServerIntegrationTestHarness fixture;
    public SqlServerStatutoryPayrollIntegrationTests(SqlServerIntegrationTestHarness fixture) => this.fixture = fixture;

    [SqlServerStatutoryFact, Trait("Category", "SqlServerIntegration")]
    public async Task SqlServer_statutory_framework_persists_tenant_scoped_configuration()
    {
        var tenant = Guid.NewGuid(); var code = $"SQL7F{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        try
        {
            await using var db = fixture.CreateContext(new TestTenantContext());
            db.Tenants.Add(new Tenant { Id = tenant, TenantCode = code, TenantName = code, Host = code.ToLowerInvariant() + ".test", ShardKey = code.ToLowerInvariant() + "-shard", Status = TenantStatus.Active, DatabaseProvider = DatabaseProviderType.SqlServer });
            db.StatutoryConfigurations.Add(new StatutoryConfiguration { Id = Guid.NewGuid(), TenantId = tenant, Code = "PF-TEST", Name = "PF test", JurisdictionCode = "IN", StatutoryType = StatutoryType.ProvidentFund });
            await db.SaveChangesAsync();
            await using var read = fixture.CreateContext(new TestTenantContext(tenant));
            Assert.Equal("PF-TEST", await read.StatutoryConfigurations.Where(x => x.TenantId == tenant).Select(x => x.Code).SingleAsync());
        }
        finally
        {
            await using var cleanup = fixture.CreateContext(new TestTenantContext());
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [StatutoryConfigurationHistories] WHERE [TenantId] = {tenant}");
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [StatutoryComponentBasis] WHERE [TenantId] = {tenant}");
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [StatutorySlabs] WHERE [TenantId] = {tenant}");
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [StatutoryConfigurationVersions] WHERE [TenantId] = {tenant}");
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [EmployeeStatutoryProfileHistories] WHERE [TenantId] = {tenant}");
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [EmployeeStatutoryProfiles] WHERE [TenantId] = {tenant}");
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [PayrollStatutoryResults] WHERE [TenantId] = {tenant}");
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [StatutoryConfigurations] WHERE [TenantId] = {tenant}");
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [Tenants] WHERE [Id] = {tenant}");
        }
    }
}

public sealed class SqlServerStatutoryFactAttribute : FactAttribute
{
    public SqlServerStatutoryFactAttribute() => Skip = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SqlServerIntegrationTestHarness.RequiredEnvironmentVariable)) ? $"SQL Server Statutory tests not executed: {SqlServerIntegrationTestHarness.RequiredEnvironmentVariable} is absent." : null;
}
