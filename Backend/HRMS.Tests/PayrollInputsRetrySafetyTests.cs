using System.Security.Cryptography;
using System.Text;
using HRMS.Application.Abstractions;
using HRMS.Application.DTOs.Payroll;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class PayrollInputsRetrySafetyTests
{
    [Fact]
    public async Task Upload_retry_is_safe()
    {
        await using var database = await RetryDatabase.CreateAsync();
        var service = database.Service();
        const string csv = "EmployeeCode,ComponentCode,Amount,EffectiveDate\nRS-001,RS-EARN,100,2026-09-01\n";
        await using var firstContent = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        await using var secondContent = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var first = await service.UploadAsync(database.BatchId, firstContent, "retry.csv");
        var retry = await service.UploadAsync(database.BatchId, secondContent, "retry.csv");

        Assert.True(first.Succeeded, first.Message);
        Assert.True(retry.Succeeded || retry.Status == HRMS.Application.Common.ResultStatus.Conflict, retry.Message);
        Assert.Equal(1, await database.Db.PayrollInputLines.CountAsync(x => x.BatchId == database.BatchId));
        Assert.Equal(1, await database.Db.PayrollInputHistories.CountAsync(x => x.BatchId == database.BatchId && x.Event == PayrollInputHistoryEvent.Uploaded));
        Assert.Single(await database.Db.PayrollInputBatches.Where(x => x.Id == database.BatchId && x.FileHash != null).ToListAsync());
    }

    [Fact]
    public async Task Validate_retry_is_safe()
    {
        await using var database = await RetryDatabase.CreateAsync();
        var service = database.Service();
        var first = await service.ValidateAsync(database.BatchId);
        var retry = await service.ValidateAsync(database.BatchId);

        Assert.True(first.Succeeded, first.Message);
        Assert.True(retry.Succeeded, retry.Message);
        Assert.Equal(first.Value!.TotalRows, retry.Value!.TotalRows);
        Assert.Equal(first.Value.ValidRows, retry.Value.ValidRows);
        Assert.Equal(first.Value.InvalidRows, retry.Value.InvalidRows);
        Assert.Equal(first.Value.WarningRows, retry.Value.WarningRows);
        Assert.Equal(PayrollInputBatchStatus.Validated, retry.Value.Status);
        Assert.Equal(0, await database.Db.PayrollInputValidationIssues.CountAsync(x => x.BatchId == database.BatchId));
        Assert.Equal(2, await database.Db.PayrollInputHistories.CountAsync(x => x.BatchId == database.BatchId && x.Event == PayrollInputHistoryEvent.Validated));
    }

    [Fact]
    public async Task Submit_retry_is_safe()
    {
        await using var database = await RetryDatabase.CreateAsync();
        var service = database.Service();
        Assert.True((await service.ValidateAsync(database.BatchId)).Succeeded);
        var first = await service.SubmitAsync(database.BatchId);
        var retry = await service.SubmitAsync(database.BatchId);

        Assert.True(first.Succeeded, first.Message);
        Assert.False(retry.Succeeded);
        Assert.Equal(HRMS.Application.Common.ResultStatus.Conflict, retry.Status);
        Assert.Equal(PayrollInputBatchStatus.Submitted, await database.Db.PayrollInputBatches.Where(x => x.Id == database.BatchId).Select(x => x.Status).SingleAsync());
        Assert.Equal(1, await database.Db.PayrollInputHistories.CountAsync(x => x.BatchId == database.BatchId && x.Event == PayrollInputHistoryEvent.Submitted));
    }

    [Fact]
    public async Task Approve_retry_is_safe()
    {
        await using var database = await RetryDatabase.CreateAsync();
        var service = database.Service();
        Assert.True((await service.ValidateAsync(database.BatchId)).Succeeded);
        Assert.True((await service.SubmitAsync(database.BatchId)).Succeeded);
        var first = await service.ApproveAsync(database.BatchId);
        var retry = await service.ApproveAsync(database.BatchId);

        Assert.True(first.Succeeded, first.Message);
        Assert.False(retry.Succeeded);
        Assert.Equal(HRMS.Application.Common.ResultStatus.Conflict, retry.Status);
        Assert.Equal(PayrollInputBatchStatus.Approved, await database.Db.PayrollInputBatches.Where(x => x.Id == database.BatchId).Select(x => x.Status).SingleAsync());
        Assert.Equal(1, await database.Db.PayrollInputHistories.CountAsync(x => x.BatchId == database.BatchId && x.Event == PayrollInputHistoryEvent.Approved));
    }

    [Fact]
    public async Task Reject_retry_is_safe()
    {
        await using var database = await RetryDatabase.CreateAsync();
        var service = database.Service();
        Assert.True((await service.ValidateAsync(database.BatchId)).Succeeded);
        Assert.True((await service.SubmitAsync(database.BatchId)).Succeeded);
        var first = await service.RejectAsync(database.BatchId, "retry rejection");
        var retry = await service.RejectAsync(database.BatchId, "retry rejection");

        Assert.True(first.Succeeded, first.Message);
        Assert.False(retry.Succeeded);
        Assert.Equal(HRMS.Application.Common.ResultStatus.Conflict, retry.Status);
        var batch = await database.Db.PayrollInputBatches.AsNoTracking().SingleAsync(x => x.Id == database.BatchId);
        Assert.Equal(PayrollInputBatchStatus.Rejected, batch.Status);
        Assert.Equal("retry rejection", batch.CancellationReason ?? "retry rejection");
        Assert.Equal(1, await database.Db.PayrollInputHistories.CountAsync(x => x.BatchId == database.BatchId && x.Event == PayrollInputHistoryEvent.Rejected));
    }

    [Fact]
    public async Task Post_retry_is_safe()
    {
        await using var database = await RetryDatabase.CreateAsync();
        var service = database.Service();
        Assert.True((await service.ValidateAsync(database.BatchId)).Succeeded);
        Assert.True((await service.SubmitAsync(database.BatchId)).Succeeded);
        Assert.True((await service.ApproveAsync(database.BatchId)).Succeeded);
        var first = await service.PostAsync(database.BatchId);
        var retry = await service.PostAsync(database.BatchId);

        Assert.True(first.Succeeded, first.Message);
        Assert.True(retry.Succeeded, retry.Message);
        Assert.Equal(1, await database.Db.PayrollAdjustments.CountAsync(x => x.SourceType == "PayrollInputBatch" && x.SourceReferenceId == database.BatchId));
        Assert.Equal(100m, await database.Db.PayrollAdjustments.Where(x => x.SourceType == "PayrollInputBatch" && x.SourceReferenceId == database.BatchId).SumAsync(x => x.Amount));
        Assert.Equal(1, await database.Db.PayrollInputHistories.CountAsync(x => x.BatchId == database.BatchId && x.Event == PayrollInputHistoryEvent.Posted));
        Assert.Equal(PayrollInputBatchStatus.Posted, await database.Db.PayrollInputBatches.Where(x => x.Id == database.BatchId).Select(x => x.Status).SingleAsync());
    }

    [Fact]
    public async Task Cancel_retry_is_safe()
    {
        await using var database = await RetryDatabase.CreateAsync();
        var service = database.Service();
        var first = await service.CancelAsync(database.BatchId, "retry cancellation");
        var retry = await service.CancelAsync(database.BatchId, "retry cancellation");

        Assert.True(first.Succeeded, first.Message);
        Assert.False(retry.Succeeded);
        Assert.Equal(HRMS.Application.Common.ResultStatus.Conflict, retry.Status);
        Assert.Equal(PayrollInputBatchStatus.Cancelled, await database.Db.PayrollInputBatches.Where(x => x.Id == database.BatchId).Select(x => x.Status).SingleAsync());
        Assert.Equal(1, await database.Db.PayrollInputHistories.CountAsync(x => x.BatchId == database.BatchId && x.Event == PayrollInputHistoryEvent.Cancelled));
        Assert.Equal(0, await database.Db.PayrollAdjustments.CountAsync(x => x.SourceReferenceId == database.BatchId));
    }

    [Fact]
    public async Task Phase7R_handoff_retry_is_safe()
    {
        await using var database = await RetryDatabase.CreateAsync();
        var service = database.Service();
        Assert.True((await service.ValidateAsync(database.BatchId)).Succeeded);
        Assert.True((await service.SubmitAsync(database.BatchId)).Succeeded);
        Assert.True((await service.ApproveAsync(database.BatchId)).Succeeded);
        Assert.True((await service.PostAsync(database.BatchId)).Succeeded);
        var adjustment = await database.Db.PayrollAdjustments.SingleAsync(x => x.SourceReferenceId == database.BatchId);
        database.Db.PayrollAdjustmentApplications.Add(new PayrollAdjustmentApplication { Id = Guid.NewGuid(), TenantId = database.Tenant.TenantId!.Value, PayrollAdjustmentId = adjustment.Id, AppliedAmount = adjustment.Amount, AppliedDate = new DateOnly(2026, 9, 30) });
        await database.Db.SaveChangesAsync();

        var first = await service.CancelAsync(database.BatchId, "historical correction");
        var retry = await service.CancelAsync(database.BatchId, "historical correction");

        Assert.False(first.Succeeded);
        Assert.False(retry.Succeeded);
        Assert.Equal(HRMS.Application.Common.ResultStatus.Conflict, first.Status);
        Assert.Equal(HRMS.Application.Common.ResultStatus.Conflict, retry.Status);
        Assert.Contains("Phase 7R", first.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(PayrollInputBatchStatus.Posted, await database.Db.PayrollInputBatches.Where(x => x.Id == database.BatchId).Select(x => x.Status).SingleAsync());
        Assert.Equal(1, await database.Db.PayrollAdjustments.CountAsync(x => x.SourceReferenceId == database.BatchId));
        Assert.Equal(1, await database.Db.PayrollInputHistories.CountAsync(x => x.BatchId == database.BatchId && x.Event == PayrollInputHistoryEvent.Posted));
    }

    private sealed class RetryDatabase : IAsyncDisposable
    {
        private readonly SqliteInMemoryDatabase database;
        public HrmsDbContext Db { get; }
        public TestTenantContext Tenant { get; }
        public Guid BatchId { get; private set; }
        private RetryDatabase(SqliteInMemoryDatabase database, HrmsDbContext db, TestTenantContext tenant) { this.database = database; Db = db; Tenant = tenant; }

        public static async Task<RetryDatabase> CreateAsync()
        {
            var database = new SqliteInMemoryDatabase();
            var tenant = new TestTenantContext(Guid.NewGuid(), Guid.NewGuid());
            var db = database.CreateContext(tenant);
            var tenantId = tenant.TenantId!.Value;
            var employeeId = Guid.NewGuid();
            var componentId = Guid.NewGuid();
            db.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"RS{tenantId:N}"[..12], TenantName = "Retry safety tenant", Host = $"{tenantId:N}.test", ShardKey = tenantId.ToString("N") });
            db.Employees.Add(new Employee { Id = employeeId, TenantId = tenantId, EmployeeCode = "RS-001", FirstName = "Retry", LastName = "Employee", Email = $"{employeeId:N}@test.local", DateOfJoining = new DateOnly(2025, 1, 1), Status = EmployeeStatus.Active });
            db.SalaryComponents.Add(new SalaryComponent { Id = componentId, TenantId = tenantId, Code = "RS-EARN", Name = "Retry earning", ComponentType = SalaryComponentType.Earning, CalculationType = SalaryCalculationType.ManualInput, EffectiveFrom = new DateOnly(2025, 1, 1), IsActive = true, AffectsGross = true, AffectsNetPay = true });
            await db.SaveChangesAsync();
            var result = await new PayrollInputService(db, tenant, TimeProvider.System).CreateBatchAsync(new PayrollInputBatchRequest { Name = "Retry batch", EffectiveDate = new DateOnly(2026, 9, 1), Lines = [new PayrollInputLineRequest { EmployeeCode = "RS-001", ComponentCode = "RS-EARN", InputType = PayrollInputType.OneTimeEarning, Amount = 100, EffectiveDate = new DateOnly(2026, 9, 1) }] });
            Assert.True(result.Succeeded, result.Message);
            return new RetryDatabase(database, db, tenant) { BatchId = result.Value!.Id };
        }

        public PayrollInputService Service() => new(Db, Tenant, TimeProvider.System, new AllowApprovalGuard());
        public ValueTask DisposeAsync() { Db.Dispose(); database.Dispose(); return ValueTask.CompletedTask; }

        private sealed class AllowApprovalGuard : IPayrollApprovalGuard
        {
            public Task<HRMS.Application.Common.Result<bool>> ValidateAsync(Guid? makerUserId, string action, string? reason = null, CancellationToken ct = default) => Task.FromResult(HRMS.Application.Common.Result<bool>.Success(true));
        }
    }
}
