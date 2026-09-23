using System.Diagnostics;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class PayrollProductionClosureLargeDataTests
{
    [Fact]
    public async Task Ten_thousand_employee_production_health_scan_is_bounded_and_tenant_scoped()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid();
        await using (var seed = database.CreateContext(new TestTenantContext()))
        {
            seed.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"L{tenantId:N}"[..12], TenantName = "Large closure", Host = $"{tenantId:N}.test", ShardKey = tenantId.ToString("N") });
            seed.Employees.AddRange(Enumerable.Range(1, 10_000).Select(index => new Employee
            {
                Id = Guid.NewGuid(), TenantId = tenantId, EmployeeCode = $"LC{index:00000}", FirstName = "Large", LastName = $"Employee {index}", Email = $"large-closure-{index}@test.local", DateOfJoining = new DateOnly(2020, 1, 1), Status = EmployeeStatus.Active
            }));
            await seed.SaveChangesAsync();
        }

        var stopwatch = Stopwatch.StartNew();
        await using var db = database.CreateContext(new TestTenantContext(tenantId));
        var result = await new PayrollOperationsService(db, new TestTenantContext(tenantId)).GetProductionHealthAsync();
        stopwatch.Stop();

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(10_000, await db.Employees.CountAsync());
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(30));
    }
}
