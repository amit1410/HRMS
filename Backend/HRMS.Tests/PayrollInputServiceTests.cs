using System.Text;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class PayrollInputServiceTests
{
    [Fact]
    public async Task Staged_line_can_be_updated_and_deleted_only_while_editable()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var tenant = new TestTenantContext(tenantId, actorId);
        await using var db = database.CreateContext(tenant);
        await AddTenantAsync(db, tenantId);
        var service = new PayrollInputService(db, tenant, TimeProvider.System);

        var created = await service.CreateBatchAsync(new PayrollInputBatchRequest
        {
            Name = "Manual inputs",
            EffectiveDate = new(2026, 9, 1),
            Lines = [new PayrollInputLineRequest { EmployeeCode = "E-001", ComponentCode = "BONUS", InputType = PayrollInputType.OneTimeEarning, Amount = 100, EffectiveDate = new(2026, 9, 1) }]
        });
        Assert.True(created.Succeeded, created.Message);
        var lineId = await db.PayrollInputLines.Select(x => x.Id).SingleAsync();

        var updated = await service.UpdateLineAsync(created.Value!.Id, lineId, new PayrollInputLineUpdateRequest
        {
            EmployeeCode = "E-002", ComponentCode = "AWARD", InputType = PayrollInputType.OneTimeEarning, Amount = 250, EffectiveDate = new(2026, 9, 2), Remarks = "corrected"
        });
        Assert.True(updated.Succeeded, updated.Message);
        Assert.Equal("E-002", updated.Value!.EmployeeCode);
        Assert.Equal(PayrollInputLineStatus.Staged, updated.Value.Status);

        var deleted = await service.DeleteLineAsync(created.Value.Id, lineId);
        Assert.True(deleted.Succeeded, deleted.Message);
        Assert.Empty(await db.PayrollInputLines.ToListAsync());
        Assert.Contains(await db.PayrollInputHistories.ToListAsync(), x => x.Event == PayrollInputHistoryEvent.Corrected);
    }

    [Fact]
    public async Task Csv_upload_preserves_quoted_commas_and_utf8_values()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenant = new TestTenantContext(Guid.NewGuid(), Guid.NewGuid());
        await using var db = database.CreateContext(tenant);
        await AddTenantAsync(db, tenant.TenantId!.Value);
        var service = new PayrollInputService(db, tenant, TimeProvider.System);
        var created = await service.CreateBatchAsync(new PayrollInputBatchRequest { Name = "CSV", EffectiveDate = new(2026, 9, 1), SourceType = PayrollInputSourceType.Csv });
        Assert.True(created.Succeeded, created.Message);

        const string csv = "EmployeeCode,ComponentCode,Amount,EffectiveDate,Remarks\r\nE-001,BONUS,100,2026-09-01,\"Award, café\"\r\n";
        await using var content = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var uploaded = await service.UploadAsync(created.Value!.Id, content, "safe.csv");

        Assert.True(uploaded.Succeeded, uploaded.Message);
        var line = await db.PayrollInputLines.SingleAsync();
        Assert.Equal("Award, café", line.Remarks);
        Assert.Equal("safe.csv", uploaded.Value!.FileName);
    }

    [Fact]
    public async Task Tenant_isolation_prevents_cross_tenant_batch_access()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantA = new TestTenantContext(Guid.NewGuid(), Guid.NewGuid());
        await using var dbA = database.CreateContext(tenantA);
        await AddTenantAsync(dbA, tenantA.TenantId!.Value);
        var serviceA = new PayrollInputService(dbA, tenantA, TimeProvider.System);
        var created = await serviceA.CreateBatchAsync(new PayrollInputBatchRequest { Name = "Tenant A", EffectiveDate = new(2026, 9, 1) });
        Assert.True(created.Succeeded, created.Message);

        var tenantB = new TestTenantContext(Guid.NewGuid(), Guid.NewGuid());
        await using var dbB = database.CreateContext(tenantB);
        var serviceB = new PayrollInputService(dbB, tenantB, TimeProvider.System);
        var batches = await serviceB.GetBatchesAsync(new PayrollInputBatchQuery());
        var update = await serviceB.UpdateLineAsync(created.Value!.Id, Guid.NewGuid(), new PayrollInputLineUpdateRequest());

        Assert.True(batches.Succeeded);
        Assert.Empty(batches.Value!.Items);
        Assert.False(update.Succeeded);
        Assert.Equal(ResultStatus.NotFound, update.Status);
    }

    [Fact]
    public async Task Template_update_after_submission_creates_a_new_version()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenant = new TestTenantContext(Guid.NewGuid(), Guid.NewGuid());
        await using var db = database.CreateContext(tenant);
        await AddTenantAsync(db, tenant.TenantId!.Value);
        var service = new PayrollInputService(db, tenant, TimeProvider.System);
        var request = new PayrollInputTemplateRequest
        {
            Code = "BONUS_CSV", Name = "Bonus CSV", InputType = PayrollInputType.OneTimeEarning,
            Columns = [new PayrollInputTemplateColumnRequest { SourceColumnName = "EmployeeCode", TargetField = "EmployeeCode", Required = true, Position = 1 }]
        };
        var template = await service.CreateTemplateAsync(request);
        Assert.True(template.Succeeded, template.Message);
        var batch = await service.CreateBatchAsync(new PayrollInputBatchRequest { Name = "Uses template", EffectiveDate = new(2026, 9, 1), TemplateId = template.Value!.Id });
        Assert.True(batch.Succeeded, batch.Message);
        Assert.True((await service.ValidateAsync(batch.Value!.Id)).Succeeded);
        Assert.True((await service.SubmitAsync(batch.Value.Id)).Succeeded);

        request.Name = "Bonus CSV v2";
        var updated = await service.UpdateTemplateAsync(template.Value.Id, request);

        Assert.True(updated.Succeeded, updated.Message);
        Assert.Equal(2, updated.Value!.Version);
        Assert.Equal(template.Value.Code, updated.Value.Code);
        Assert.Equal(2, await db.PayrollInputTemplates.CountAsync(x => x.Code == "BONUS_CSV"));
    }

    private static async Task AddTenantAsync(HRMS.Infrastructure.Persistence.HrmsDbContext db, Guid tenantId)
    {
        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            TenantCode = $"T{tenantId:N}"[..20],
            Host = $"{tenantId:N}.test",
            ShardKey = $"test-{tenantId:N}",
            TenantName = "Payroll input test tenant",
            Status = TenantStatus.Active
        });
        await db.SaveChangesAsync();
    }
}
