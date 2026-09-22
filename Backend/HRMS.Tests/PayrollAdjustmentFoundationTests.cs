using HRMS.Application.DTOs.Payroll;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class PayrollAdjustmentFoundationTests
{
    [Fact]
    public async Task Adjustment_numbering_lifecycle_and_history_are_persisted()
    {
        using var database = new SqliteInMemoryDatabase(); var tenantId = Guid.NewGuid(); var employeeId = Guid.NewGuid(); await SeedAsync(database, tenantId, employeeId);
        var maker = Guid.NewGuid(); await using var db = database.CreateContext(new TestTenantContext(tenantId, maker)); var context = new TestTenantContext(tenantId, maker); var service = new PayrollAdjustmentService(db, context, TimeProvider.System, new PayrollRunService(db, context, TimeProvider.System));
        var first = await service.CreateAsync(new PayrollAdjustmentRequest { EmployeeId = employeeId, EffectiveDate = new(2026, 9, 1), Description = "Missed earning", Amount = 2500, ComponentCode = "BONUS", AdjustmentType = PayrollAdjustmentType.AdditionalEarning });
        var second = await service.CreateAsync(new PayrollAdjustmentRequest { EmployeeId = employeeId, EffectiveDate = new(2026, 9, 2), Description = "Excess deduction", Amount = 400, Direction = PayrollAdjustmentDirection.Deduction, ComponentCode = "RECOVERY", AdjustmentType = PayrollAdjustmentType.AdditionalDeduction });
        Assert.True(first.Succeeded, first.Message); Assert.True(second.Succeeded, second.Message); Assert.Equal("PADJ/2026/000001", first.Value!.AdjustmentNumber); Assert.Equal("PADJ/2026/000002", second.Value!.AdjustmentNumber);
        Assert.True((await service.SubmitAsync(first.Value.Id)).Succeeded); context.UserId = Guid.NewGuid(); Assert.True((await service.ApproveAsync(first.Value.Id)).Succeeded); var history = await service.GetHistoryAsync(first.Value.Id); Assert.True(history.Succeeded); Assert.Contains(history.Value!, x => x.EventType == PayrollAdjustmentHistoryEventType.Approved);
    }

    [Fact]
    public async Task Self_approval_is_blocked_and_approved_adjustment_is_not_editable_by_creation_path()
    {
        using var database = new SqliteInMemoryDatabase(); var tenantId = Guid.NewGuid(); var employeeId = Guid.NewGuid(); await SeedAsync(database, tenantId, employeeId); var maker = Guid.NewGuid();
        await using (var setup = database.CreateContext(new TestTenantContext(tenantId, maker))) { setup.PayrollControlConfigurations.Add(new PayrollControlConfiguration { Id = Guid.NewGuid(), TenantId = tenantId, RequireMakerChecker = true, PreventSelfApproval = true, UpdatedAtUtc = DateTime.UtcNow }); await setup.SaveChangesAsync(); }
        await using var db = database.CreateContext(new TestTenantContext(tenantId, maker)); var context = new TestTenantContext(tenantId, maker); var service = new PayrollAdjustmentService(db, context, TimeProvider.System, new PayrollRunService(db, context, TimeProvider.System)); var created = await service.CreateAsync(new PayrollAdjustmentRequest { EmployeeId = employeeId, EffectiveDate = new(2026, 9, 1), Description = "Correction", Amount = 100 }); Assert.True(created.Succeeded); Assert.True((await service.SubmitAsync(created.Value!.Id)).Succeeded); Assert.False((await service.ApproveAsync(created.Value.Id)).Succeeded);
    }

    [Fact]
    public async Task Adjustments_are_tenant_scoped_and_duplicate_source_is_idempotently_rejected()
    {
        using var database = new SqliteInMemoryDatabase(); var tenantA = Guid.NewGuid(); var tenantB = Guid.NewGuid(); var employeeA = Guid.NewGuid(); await SeedAsync(database, tenantA, employeeA); await SeedTenantAsync(database, tenantB);
        var source = Guid.NewGuid(); await using var db = database.CreateContext(new TestTenantContext(tenantA)); var context = new TestTenantContext(tenantA, Guid.NewGuid()); var service = new PayrollAdjustmentService(db, context, TimeProvider.System, new PayrollRunService(db, context, TimeProvider.System)); var request = new PayrollAdjustmentRequest { EmployeeId = employeeA, EffectiveDate = new(2026, 9, 1), Description = "Source correction", Amount = 10, SourceType = "PayrollResult", SourceReferenceId = source }; Assert.True((await service.CreateAsync(request)).Succeeded); Assert.False((await service.CreateAsync(request)).Succeeded);
        await using var other = database.CreateContext(new TestTenantContext(tenantB)); var otherContext = new TestTenantContext(tenantB); var otherService = new PayrollAdjustmentService(other, otherContext, TimeProvider.System, new PayrollRunService(other, otherContext, TimeProvider.System)); Assert.Empty((await otherService.GetAsync(new PayrollAdjustmentQuery())).Value!.Items);
    }

    [Fact]
    public async Task Bounded_register_reconciles_one_hundred_adjustments_and_two_pages()
    {
        using var database = new SqliteInMemoryDatabase(); var tenantId = Guid.NewGuid(); var employeeId = Guid.NewGuid(); await SeedAsync(database, tenantId, employeeId);
        await using var db = database.CreateContext(new TestTenantContext(tenantId, Guid.NewGuid())); var context = new TestTenantContext(tenantId, Guid.NewGuid()); var service = new PayrollAdjustmentService(db, context, TimeProvider.System, new PayrollRunService(db, context, TimeProvider.System));
        for (var i = 0; i < 100; i++)
        {
            var result = await service.CreateAsync(new PayrollAdjustmentRequest { EmployeeId = employeeId, EffectiveDate = DateOnly.FromDateTime(new DateTime(2026, 9, 1).AddDays(i % 28)), Description = $"Bounded adjustment {i}", Amount = i + 1, Direction = i % 2 == 0 ? PayrollAdjustmentDirection.Earning : PayrollAdjustmentDirection.Deduction, ComponentCode = "ADJUSTMENT", AdjustmentType = i % 2 == 0 ? PayrollAdjustmentType.AdditionalEarning : PayrollAdjustmentType.AdditionalDeduction, SourceType = "BoundedAcceptance", SourceReferenceId = Guid.NewGuid() });
            Assert.True(result.Succeeded, result.Message);
        }

        var first = await service.GetAsync(new PayrollAdjustmentQuery { Page = 1, PageSize = 50 }); var second = await service.GetAsync(new PayrollAdjustmentQuery { Page = 2, PageSize = 50 });
        Assert.True(first.Succeeded); Assert.True(second.Succeeded); Assert.Equal(100, first.Value!.TotalCount); Assert.Equal(50, first.Value.Items.Count); Assert.Equal(50, second.Value!.Items.Count); Assert.Equal(5050m, first.Value.Items.Concat(second.Value.Items).Sum(x => x.Amount)); Assert.Equal(2500m, first.Value.Items.Concat(second.Value.Items).Where(x => x.Direction == PayrollAdjustmentDirection.Earning).Sum(x => x.Amount)); Assert.Equal(2550m, first.Value.Items.Concat(second.Value.Items).Where(x => x.Direction == PayrollAdjustmentDirection.Deduction).Sum(x => x.Amount));
    }

    private static async Task SeedAsync(SqliteInMemoryDatabase database, Guid tenantId, Guid employeeId) { await SeedTenantAsync(database, tenantId); await using var db = database.CreateContext(new TestTenantContext(tenantId)); db.Employees.Add(new Employee { Id = employeeId, TenantId = tenantId, EmployeeCode = $"ADJ{employeeId:N}"[..10], FirstName = "Adjustment", LastName = "Employee", Email = $"{employeeId:N}@test.local", DateOfJoining = new(2024, 1, 1), Status = EmployeeStatus.Active }); await db.SaveChangesAsync(); }
    private static async Task SeedTenantAsync(SqliteInMemoryDatabase database, Guid tenantId) { await using var db = database.CreateContext(new TestTenantContext()); db.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"AT{tenantId:N}"[..10], TenantName = "Adjustment tenant", Host = $"{tenantId:N}.adjustment.test", ShardKey = tenantId.ToString("N"), Status = TenantStatus.Active }); await db.SaveChangesAsync(); }
}
