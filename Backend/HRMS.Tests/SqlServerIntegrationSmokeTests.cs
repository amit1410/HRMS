using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

/// <summary>Minimal SQL Server fixture checks. Full Employee Code scenarios are layered on this fixture.</summary>
public sealed class SqlServerIntegrationSmokeTests : IClassFixture<SqlServerIntegrationTestHarness>
{
    private readonly SqlServerIntegrationTestHarness _fixture;
    private static readonly Guid Tenant = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    public SqlServerIntegrationSmokeTests(SqlServerIntegrationTestHarness fixture) => _fixture = fixture;

    [Fact, Trait("Category", "SqlServerIntegration")]
    public async Task Dedicated_database_supports_connections_and_transaction_rollback()
    {
        if (!_fixture.IsConfigured) return;
        var tenant = new TestTenantContext(Tenant);
        await using var context = _fixture.CreateContext(tenant);
        Assert.True(await context.Database.CanConnectAsync());
        if (!await context.Tenants.AnyAsync(t => t.Id == Tenant))
        {
            context.Tenants.Add(new Tenant { Id = Tenant, TenantCode = "ITEST", Host = "itest.local", ShardKey = "itest", TenantName = "Integration Test", Status = TenantStatus.Active });
            await context.SaveChangesAsync();
        }
        await using (var transaction = await context.BeginTransactionAsync())
        {
            var id = Guid.NewGuid();
            context.Employees.Add(new Employee { Id = id, TenantId = Tenant, FirstName = "Integration", LastName = "Rollback", Email = $"{id:N}@test.invalid", DateOfJoining = new DateOnly(2026, 9, 1) });
            await context.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        await using var fresh = _fixture.CreateContext(new TestTenantContext(Tenant));
        Assert.Equal(0, await fresh.Employees.IgnoreQueryFilters().CountAsync(e => e.FirstName == "Integration" && e.LastName == "Rollback"));
    }

    [Fact, Trait("Category", "SqlServerIntegration")]
    public async Task Two_independent_contexts_can_connect()
    {
        if (!_fixture.IsConfigured) return;
        await using var first = _fixture.CreateContext(new TestTenantContext(Tenant));
        await using var second = _fixture.CreateContext(new TestTenantContext(Tenant));
        Assert.True(await first.Database.CanConnectAsync());
        Assert.True(await second.Database.CanConnectAsync());
        Assert.Equal(await first.Employees.CountAsync(), await second.Employees.CountAsync());
    }

    [Fact, Trait("Category", "SqlServerIntegration")]
    public async Task Synthetic_employee_can_be_inserted_queried_and_deleted()
    {
        if (!_fixture.IsConfigured) return;
        await using var context = _fixture.CreateContext(new TestTenantContext(Tenant));
        var id = Guid.NewGuid();
        context.Employees.Add(new Employee { Id = id, TenantId = Tenant, FirstName = "Integration", LastName = "Smoke", Email = $"{id:N}@test.invalid", DateOfJoining = new DateOnly(2026, 9, 1) });
        await context.SaveChangesAsync();
        await using var reader = _fixture.CreateContext(new TestTenantContext(Tenant));
        Assert.NotNull(await reader.Employees.SingleAsync(e => e.Id == id));
        context.Employees.Remove(await context.Employees.SingleAsync(e => e.Id == id));
        await context.SaveChangesAsync();
        Assert.Null(await reader.Employees.SingleOrDefaultAsync(e => e.Id == id));
    }
}
