using System.Security.Cryptography;
using System.Text;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class PayrollInputsConcurrencyTests
{
    [Fact]
    public async Task Validate_vs_line_edit()
    {
        await using var database = await SharedDatabase.CreateAsync(PayrollInputBatchStatus.Draft, PayrollInputLineStatus.Staged);
        var first = new PayrollInputService(database.CreateContext(), database.Tenant, TimeProvider.System);
        var second = new PayrollInputService(database.CreateContext(), database.Tenant, TimeProvider.System);
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var validation = RunAfterGate(gate.Task, () => first.ValidateAsync(database.BatchId));
        var edit = RunAfterGate(gate.Task, () => second.UpdateLineAsync(database.BatchId, database.LineId, ValidLine("edited")));
        gate.SetResult(true);
        await Task.WhenAll(validation, edit);
        var validationResult = await validation;
        var editResult = await edit;
        Assert.Null(validationResult.Exception);
        Assert.Null(editResult.Exception);
        database.Observer.ChangeTracker.Clear();
        var final = await database.Observer.PayrollInputLines.SingleAsync(x => x.Id == database.LineId);
        if (editResult.Value?.Succeeded == true)
        {
            Assert.Equal("edited", final.Remarks);
            Assert.Equal(PayrollInputBatchStatus.Draft, (await database.Observer.PayrollInputBatches.SingleAsync(x => x.Id == database.BatchId)).Status);
        }
        else
        {
            Assert.Equal(PayrollInputBatchStatus.Validated, (await database.Observer.PayrollInputBatches.SingleAsync(x => x.Id == database.BatchId)).Status);
        }
        Assert.Empty(await database.Observer.PayrollInputValidationIssues.Where(x => x.LineId == database.LineId).ToListAsync());
    }

    [Fact]
    public async Task Submit_vs_line_edit()
    {
        await using var database = await SharedDatabase.CreateAsync(PayrollInputBatchStatus.Validated, PayrollInputLineStatus.Valid);
        var first = new PayrollInputService(database.CreateContext(), database.Tenant, TimeProvider.System);
        var second = new PayrollInputService(database.CreateContext(), database.Tenant, TimeProvider.System);
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var submit = RunAfterGate(gate.Task, () => first.SubmitAsync(database.BatchId));
        var edit = RunAfterGate(gate.Task, () => second.UpdateLineAsync(database.BatchId, database.LineId, ValidLine("edited-after-submit")));
        gate.SetResult(true);
        await Task.WhenAll(submit, edit);
        var submitResult = await submit;
        var editResult = await edit;
        Assert.Null(submitResult.Exception);
        Assert.Null(editResult.Exception);
        var batch = await database.Observer.PayrollInputBatches.SingleAsync(x => x.Id == database.BatchId);
        Assert.Contains(batch.Status, new[] { PayrollInputBatchStatus.Validated, PayrollInputBatchStatus.Submitted, PayrollInputBatchStatus.Draft });
        if (batch.Status == PayrollInputBatchStatus.Submitted)
            Assert.Equal(PayrollInputLineStatus.Valid, await database.Observer.PayrollInputLines.Where(x => x.Id == database.LineId).Select(x => x.Status).SingleAsync());
    }

    [Fact]
    public async Task Approve_vs_reject()
    {
        await using var database = await SharedDatabase.CreateAsync(PayrollInputBatchStatus.Submitted, PayrollInputLineStatus.Valid);
        var first = new PayrollInputService(database.CreateContext(), database.Tenant, TimeProvider.System);
        var second = new PayrollInputService(database.CreateContext(), database.Tenant, TimeProvider.System);
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var approve = RunAfterGate(gate.Task, () => first.ApproveAsync(database.BatchId));
        var reject = RunAfterGate(gate.Task, () => second.RejectAsync(database.BatchId, "concurrent review"));
        gate.SetResult(true);
        var results = await Task.WhenAll(approve, reject);
        Assert.All(results, result => Assert.True(result.Exception is null, result.Exception?.ToString()));
        var status = await database.Observer.PayrollInputBatches.Where(x => x.Id == database.BatchId).Select(x => x.Status).SingleAsync();
        Assert.Contains(status, new[] { PayrollInputBatchStatus.Approved, PayrollInputBatchStatus.Rejected });
        Assert.Equal(1, await database.Observer.PayrollInputHistories.CountAsync(x => x.BatchId == database.BatchId && (x.Event == PayrollInputHistoryEvent.Approved || x.Event == PayrollInputHistoryEvent.Rejected)));
    }

    [Fact]
    public async Task Approve_vs_approve()
    {
        await using var database = await SharedDatabase.CreateAsync(PayrollInputBatchStatus.Submitted, PayrollInputLineStatus.Valid);
        var first = new PayrollInputService(database.CreateContext(), database.Tenant, TimeProvider.System);
        var second = new PayrollInputService(database.CreateContext(), database.Tenant, TimeProvider.System);
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var left = RunAfterGate(gate.Task, () => first.ApproveAsync(database.BatchId));
        var right = RunAfterGate(gate.Task, () => second.ApproveAsync(database.BatchId));
        gate.SetResult(true);
        var results = await Task.WhenAll(left, right);
        Assert.All(results, result => Assert.True(result.Exception is null, result.Exception?.ToString()));
        Assert.Equal(PayrollInputBatchStatus.Approved, await database.Observer.PayrollInputBatches.Where(x => x.Id == database.BatchId).Select(x => x.Status).SingleAsync());
        Assert.Equal(1, await database.Observer.PayrollInputHistories.CountAsync(x => x.BatchId == database.BatchId && x.Event == PayrollInputHistoryEvent.Approved));
    }

    [Fact]
    public async Task Post_vs_post()
    {
        await using var database = await SharedDatabase.CreateAsync(PayrollInputBatchStatus.Approved, PayrollInputLineStatus.Valid);
        var first = new PayrollInputService(database.CreateContext(), database.Tenant, TimeProvider.System);
        var second = new PayrollInputService(database.CreateContext(), database.Tenant, TimeProvider.System);
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var left = RunAfterGate(gate.Task, () => first.PostAsync(database.BatchId));
        var right = RunAfterGate(gate.Task, () => second.PostAsync(database.BatchId));
        gate.SetResult(true);
        var results = await Task.WhenAll(left, right);
        Assert.All(results, result => Assert.True(result.Exception is null, result.Exception?.ToString()));
        Assert.Contains(results, result => result.Value?.Succeeded == true);
        var adjustments = await database.Observer.PayrollAdjustments.Where(x => x.SourceType == "PayrollInputBatch" && x.SourceReferenceId == database.BatchId).ToListAsync();
        Assert.Single(adjustments);
        Assert.Equal(100m, adjustments.Sum(x => x.Amount));
        Assert.Equal(1, await database.Observer.PayrollInputHistories.CountAsync(x => x.BatchId == database.BatchId && x.Event == PayrollInputHistoryEvent.Posted));
        Assert.Equal(1, adjustments.Select(x => new { x.TenantId, x.SourceType, x.SourceId }).Distinct().Count());
    }

    [Fact]
    public async Task Post_vs_cancel()
    {
        await using var database = await SharedDatabase.CreateAsync(PayrollInputBatchStatus.Approved, PayrollInputLineStatus.Valid);
        var first = new PayrollInputService(database.CreateContext(), database.Tenant, TimeProvider.System);
        var second = new PayrollInputService(database.CreateContext(), database.Tenant, TimeProvider.System);
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var post = RunAfterGate(gate.Task, () => first.PostAsync(database.BatchId));
        var cancel = RunAfterGate(gate.Task, () => second.CancelAsync(database.BatchId, "concurrent cancellation"));
        gate.SetResult(true);
        var results = await Task.WhenAll(post, cancel);
        Assert.All(results, result => Assert.True(result.Exception is null, result.Exception?.ToString()));
        database.Observer.ChangeTracker.Clear();
        var batch = await database.Observer.PayrollInputBatches.SingleAsync(x => x.Id == database.BatchId);
        Assert.Contains(batch.Status, new[] { PayrollInputBatchStatus.Approved, PayrollInputBatchStatus.Posted, PayrollInputBatchStatus.Cancelled });
        var adjustments = await database.Observer.PayrollAdjustments.Where(x => x.SourceReferenceId == database.BatchId).ToListAsync();
        Assert.True(adjustments.Count <= 1);
        Assert.Equal(adjustments.Count, adjustments.Select(x => new { x.TenantId, x.SourceType, x.SourceId }).Distinct().Count());
        Assert.Equal(0, await database.Observer.PayrollAdjustmentApplications.CountAsync(x => adjustments.Select(a => a.Id).Contains(x.PayrollAdjustmentId)));
    }

    [Fact]
    public async Task Duplicate_file_import_race()
    {
        await using var database = await SharedDatabase.CreateAsync(PayrollInputBatchStatus.Draft, PayrollInputLineStatus.Staged);
        var secondBatchId = await database.AddDraftBatchAsync();
        const string csv = "EmployeeCode,ComponentCode,Amount,EffectiveDate\nCC-001,CC-EARN,100,2026-09-01\n";
        var first = new PayrollInputService(database.CreateContext(), database.Tenant, TimeProvider.System);
        var second = new PayrollInputService(database.CreateContext(), database.Tenant, TimeProvider.System);
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var left = RunCaptureAfterGate(gate.Task, () => UploadCapture(first, database.BatchId, csv));
        var right = RunCaptureAfterGate(gate.Task, () => UploadCapture(second, secondBatchId, csv));
        gate.SetResult(true);
        var results = await Task.WhenAll(left, right);
        Assert.All(results, result => Assert.True(result.Exception is null, result.Exception?.ToString()));
        Assert.Equal(1, results.Count(result => result.Value?.Succeeded == true));
        Assert.Equal(1, await database.Observer.PayrollInputBatches.CountAsync(x => x.FileHash != null));
        Assert.Equal(1, await database.Observer.PayrollInputLines.CountAsync());
    }

    private static PayrollInputLineUpdateRequest ValidLine(string remarks) => new() { EmployeeCode = "CC-001", ComponentCode = "CC-EARN", InputType = PayrollInputType.OneTimeEarning, Amount = 100, EffectiveDate = new DateOnly(2026, 9, 1), Remarks = remarks };

    private static async Task<OperationCapture<T>> RunAfterGate<T>(Task gate, Func<Task<Result<T>>> operation)
    {
        await gate;
        try { return new OperationCapture<T>(await operation(), null); }
        catch (Exception ex) { return new OperationCapture<T>(default, ex); }
    }

    private static async Task<OperationCapture<PayrollInputBatchDto>> UploadCapture(PayrollInputService service, Guid batchId, string csv)
    {
        await using var content = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        return await RunCapture(() => service.UploadAsync(batchId, content, "race.csv"));
    }

    private static async Task<OperationCapture<T>> RunCaptureAfterGate<T>(Task gate, Func<Task<OperationCapture<T>>> operation)
    {
        await gate;
        return await operation();
    }

    private static async Task<OperationCapture<T>> RunCapture<T>(Func<Task<Result<T>>> operation)
    {
        try { return new OperationCapture<T>(await operation(), null); }
        catch (Exception ex) { return new OperationCapture<T>(default, ex); }
    }

    private sealed record OperationCapture<T>(Result<T>? Value, Exception? Exception);

    private sealed class SharedDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection keeper;
        private readonly string connectionString;
        public TestTenantContext Tenant { get; } = new(Guid.NewGuid(), Guid.NewGuid());
        public HrmsDbContext Observer { get; private set; } = null!;
        public Guid BatchId { get; private set; }
        public Guid LineId { get; private set; }
        public Guid TemplateId { get; private set; }
        private SharedDatabase(SqliteConnection keeper, string connectionString) { this.keeper = keeper; this.connectionString = connectionString; }

        public static async Task<SharedDatabase> CreateAsync(PayrollInputBatchStatus status, PayrollInputLineStatus lineStatus)
        {
            var name = $"payroll-inputs-{Guid.NewGuid():N}";
            var connectionString = $"Data Source=file:{name};Mode=Memory;Cache=Shared";
            var keeper = new SqliteConnection(connectionString);
            await keeper.OpenAsync();
            var database = new SharedDatabase(keeper, connectionString);
            database.Observer = database.CreateContext();
            await database.Observer.Database.EnsureCreatedAsync();
            var tenantId = database.Tenant.TenantId!.Value;
            var employeeId = Guid.NewGuid();
            var componentId = Guid.NewGuid();
            database.BatchId = Guid.NewGuid();
            database.LineId = Guid.NewGuid();
            database.TemplateId = Guid.NewGuid();
            database.Observer.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"CC{tenantId:N}"[..12], TenantName = "Concurrency tenant", Host = $"{tenantId:N}.test", ShardKey = tenantId.ToString("N") });
            database.Observer.Employees.Add(new Employee { Id = employeeId, TenantId = tenantId, EmployeeCode = "CC-001", FirstName = "Concurrency", LastName = "Employee", Email = $"{employeeId:N}@test.local", DateOfJoining = new DateOnly(2025, 1, 1), Status = EmployeeStatus.Active });
            database.Observer.SalaryComponents.Add(new SalaryComponent { Id = componentId, TenantId = tenantId, Code = "CC-EARN", Name = "Concurrency earning", ComponentType = SalaryComponentType.Earning, CalculationType = SalaryCalculationType.ManualInput, EffectiveFrom = new DateOnly(2025, 1, 1), IsActive = true, AffectsGross = true, AffectsNetPay = true });
            database.Observer.PayrollInputTemplates.Add(new PayrollInputTemplate { Id = database.TemplateId, TenantId = tenantId, Code = "CC-TEMPLATE", Name = "Concurrency template", InputType = PayrollInputType.OneTimeEarning, Active = true, DateFormat = "yyyy-MM-dd" });
            database.Observer.PayrollInputBatches.Add(new PayrollInputBatch { Id = database.BatchId, TenantId = tenantId, TemplateId = database.TemplateId, BatchNumber = $"CC/{database.BatchId:N}"[..20], Name = "Concurrency batch", EffectiveDate = new DateOnly(2026, 9, 1), Status = status, TotalRows = 1, ValidRows = lineStatus == PayrollInputLineStatus.Valid ? 1 : 0, CreatedByUserId = database.Tenant.UserId });
            database.Observer.PayrollInputLines.Add(new PayrollInputLine { Id = database.LineId, TenantId = tenantId, BatchId = database.BatchId, RowNumber = 2, EmployeeId = employeeId, EmployeeCodeSnapshot = "CC-001", SalaryComponentId = componentId, ComponentCodeSnapshot = "CC-EARN", InputType = PayrollInputType.OneTimeEarning, Amount = 100, EffectiveDate = new DateOnly(2026, 9, 1), Status = lineStatus, SourceRowHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("cc"))) });
            await database.Observer.SaveChangesAsync();
            return database;
        }

        public async Task<Guid> AddDraftBatchAsync()
        {
            var id = Guid.NewGuid();
            Observer.PayrollInputBatches.Add(new PayrollInputBatch { Id = id, TenantId = Tenant.TenantId!.Value, TemplateId = TemplateId, BatchNumber = $"CC/{id:N}"[..20], Name = "Duplicate race batch", EffectiveDate = new DateOnly(2026, 9, 1), Status = PayrollInputBatchStatus.Draft, CreatedByUserId = Tenant.UserId });
            await Observer.SaveChangesAsync();
            return id;
        }

        public HrmsDbContext CreateContext() => new(new DbContextOptionsBuilder<HrmsDbContext>().UseSqlite(new SqliteConnection(connectionString)).Options, Tenant);
        public async ValueTask DisposeAsync() { await Observer.DisposeAsync(); await keeper.DisposeAsync(); }
    }
}
