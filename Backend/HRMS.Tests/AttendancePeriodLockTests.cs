using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class AttendancePeriodLockTests
{
    [Fact]
    public async Task Closed_period_guard_rejects_dates_and_ranges_but_allows_open_periods()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid(); await SeedTenant(database, tenantId);
        await using (var seed = database.CreateContext(new TestTenantContext(tenantId)))
        {
            seed.AttendancePeriods.Add(new AttendancePeriod { Id = Guid.NewGuid(), TenantId = tenantId, Year = 2026, Month = 9, StartDate = new(2026, 9, 1), EndDate = new(2026, 9, 30), Status = AttendancePeriodStatus.Closed });
            await seed.SaveChangesAsync();
        }
        await using var context = database.CreateContext(new TestTenantContext(tenantId));
        var guard = new AttendancePeriodLockService(context, new TestTenantContext(tenantId));
        Assert.Equal(HRMS.Application.Common.ResultStatus.Conflict, (await guard.EnsureDateIsOpenAsync(new(2026, 9, 15))).Status);
        Assert.Equal(HRMS.Application.Common.ResultStatus.Conflict, (await guard.EnsureRangeIsOpenAsync(new(2026, 8, 30), new(2026, 10, 2))).Status);
        Assert.True((await guard.EnsureDateIsOpenAsync(new(2026, 10, 1))).Succeeded);
    }

    private static async Task SeedTenant(SqliteInMemoryDatabase database, Guid tenantId)
    {
        await using var catalog = database.CreateCatalogContext();
        await catalog.Tenants.AddAsync(new Tenant { Id = tenantId, TenantCode = $"T{tenantId:N}"[..20], Host = $"{tenantId:N}.localhost", ShardKey = tenantId.ToString("N") });
        await catalog.SaveChangesAsync();
        await using var context = database.CreateContext(new TestTenantContext(tenantId));
        context.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"T{tenantId:N}"[..20], Host = $"{tenantId:N}.localhost", ShardKey = tenantId.ToString("N") });
        await context.SaveChangesAsync();
    }
}
