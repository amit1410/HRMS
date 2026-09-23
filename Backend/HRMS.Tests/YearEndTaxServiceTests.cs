using HRMS.Application.DTOs.Payroll;
using HRMS.Application.Common;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class YearEndTaxServiceTests
{
    [Fact]
    public async Task Run_creation_rejects_duplicate_active_tax_year()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenant = new TestTenantContext(Guid.NewGuid(), Guid.NewGuid());
        await using var db = database.CreateContext(tenant);
        await AddTenantAsync(db, tenant.TenantId!.Value);
        var service = new YearEndTaxService(db, tenant, TimeProvider.System);
        var request = Request(2026);

        var first = await service.CreateAsync(request);
        var second = await service.CreateAsync(request);

        Assert.True(first.Succeeded, first.Message);
        Assert.False(second.Succeeded);
        Assert.Equal(ResultStatus.Conflict, second.Status);
    }

    [Fact]
    public async Task Run_lifecycle_requires_calculation_before_submission_and_close()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenant = new TestTenantContext(Guid.NewGuid(), Guid.NewGuid());
        await using var db = database.CreateContext(tenant);
        await AddTenantAsync(db, tenant.TenantId!.Value);
        var service = new YearEndTaxService(db, tenant, TimeProvider.System);
        var created = await service.CreateAsync(Request(2027));
        Assert.True(created.Succeeded, created.Message);

        var submitBeforeCalculate = await service.SubmitAsync(created.Value!.Id);
        var closeBeforeApprove = await service.CloseAsync(created.Value.Id);

        Assert.False(submitBeforeCalculate.Succeeded);
        Assert.Equal(ResultStatus.Conflict, submitBeforeCalculate.Status);
        Assert.False(closeBeforeApprove.Succeeded);
        Assert.Equal(ResultStatus.Conflict, closeBeforeApprove.Status);
    }

    [Fact]
    public async Task Tenant_filter_hides_another_tenants_year_end_run()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantA = new TestTenantContext(Guid.NewGuid(), Guid.NewGuid());
        await using var dbA = database.CreateContext(tenantA);
        await AddTenantAsync(dbA, tenantA.TenantId!.Value);
        var serviceA = new YearEndTaxService(dbA, tenantA, TimeProvider.System);
        var created = await serviceA.CreateAsync(Request(2028));
        Assert.True(created.Succeeded, created.Message);

        var tenantB = new TestTenantContext(Guid.NewGuid(), Guid.NewGuid());
        await using var dbB = database.CreateContext(tenantB);
        var serviceB = new YearEndTaxService(dbB, tenantB, TimeProvider.System);
        var hidden = await serviceB.GetAsync(created.Value!.Id);

        Assert.False(hidden.Succeeded);
        Assert.Equal(ResultStatus.NotFound, hidden.Status);
    }

    [Fact]
    public void Tenant_relationships_do_not_create_shadow_tenant_id_columns()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenant = new TestTenantContext(Guid.NewGuid(), Guid.NewGuid());
        using var db = database.CreateContext(tenant);

        foreach (var entity in new[] { typeof(YearEndTaxRun), typeof(YearEndTaxEmployee), typeof(YearEndTaxPreviousEmployerInput), typeof(YearEndTaxAdjustment), typeof(YearEndTaxStatement), typeof(YearEndTaxHistory) })
        {
            var properties = db.Model.FindEntityType(entity)!.GetProperties().Select(x => x.Name).ToList();
            Assert.DoesNotContain("TenantId1", properties);
        }
    }

    private static YearEndTaxRunRequest Request(int taxYear) => new()
    {
        TaxYear = taxYear,
        TaxYearCode = $"{taxYear}-{(taxYear + 1) % 100:00}",
        StartDate = new DateOnly(taxYear, 4, 1),
        EndDate = new DateOnly(taxYear + 1, 3, 31)
    };

    private static async Task AddTenantAsync(HRMS.Infrastructure.Persistence.HrmsDbContext db, Guid tenantId)
    {
        db.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"T{tenantId:N}"[..20], Host = $"{tenantId:N}.test", ShardKey = $"test-{tenantId:N}", TenantName = "Year-end tax test tenant", Status = TenantStatus.Active });
        await db.SaveChangesAsync();
    }
}
