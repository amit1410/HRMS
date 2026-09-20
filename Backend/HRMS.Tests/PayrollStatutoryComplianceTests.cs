using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;

namespace HRMS.Tests;

public sealed class PayrollStatutoryComplianceTests
{
    [Fact]
    public async Task Compliance_period_validates_dates_and_duplicate_scope()
    {
        using var database = new SqliteInMemoryDatabase(); var tenantId = Guid.NewGuid(); await AddTenantAsync(database, tenantId); await using var db = database.CreateContext(new TestTenantContext(tenantId)); var service = new PayrollStatutoryComplianceService(db, new TestTenantContext(tenantId), TimeProvider.System);
        var invalid = await service.CreatePeriodAsync(new PayrollCompliancePeriodRequest { ComplianceType = PayrollComplianceType.ProvidentFund, PeriodStart = new DateOnly(2026, 2, 1), PeriodEnd = new DateOnly(2026, 1, 31) }); Assert.Equal(ResultStatus.ValidationFailed, invalid.Status);
        var created = await service.CreatePeriodAsync(new PayrollCompliancePeriodRequest { ComplianceType = PayrollComplianceType.ProvidentFund, PeriodStart = new DateOnly(2026, 1, 1), PeriodEnd = new DateOnly(2026, 1, 31) }); Assert.True(created.Succeeded, created.Message);
        var duplicate = await service.CreatePeriodAsync(new PayrollCompliancePeriodRequest { ComplianceType = PayrollComplianceType.ProvidentFund, PeriodStart = new DateOnly(2026, 1, 1), PeriodEnd = new DateOnly(2026, 1, 31) }); Assert.Equal(ResultStatus.Conflict, duplicate.Status);
    }

    [Fact]
    public async Task Return_generation_requires_persisted_statutory_source_and_does_not_calculate_one()
    {
        using var database = new SqliteInMemoryDatabase(); var tenantId = Guid.NewGuid(); await AddTenantAsync(database, tenantId); await using var db = database.CreateContext(new TestTenantContext(tenantId)); var service = new PayrollStatutoryComplianceService(db, new TestTenantContext(tenantId), TimeProvider.System); var period = await service.CreatePeriodAsync(new PayrollCompliancePeriodRequest { ComplianceType = PayrollComplianceType.Esi, PeriodStart = new DateOnly(2026, 1, 1), PeriodEnd = new DateOnly(2026, 1, 31) }); Assert.True(period.Succeeded); var generated = await service.GenerateAsync(period.Value!.Id); Assert.Equal(ResultStatus.Conflict, generated.Status); Assert.Contains("MissingStatutorySource", generated.Message);
    }

    private static async Task AddTenantAsync(SqliteInMemoryDatabase database, Guid id)
    { await using var db = database.CreateContext(new TestTenantContext()); db.Tenants.Add(new Tenant { Id = id, TenantCode = $"SC{id:N}"[..12], TenantName = "Compliance test tenant", Host = $"{id:N}.compliance.test", ShardKey = id.ToString("N") }); await db.SaveChangesAsync(); }
}
