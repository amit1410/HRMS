using HRMS.Application.DTOs.Payroll;
using HRMS.Application.Common;
using HRMS.Application.Abstractions;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class StatutoryFilingConcurrencyTests
{
    [Fact] public async Task Generate_vs_generate() { await using var s = await FilingScenario.CreateAsync(); var results = await Race(s, () => Service(s).GenerateAsync(s.RunId), () => Service(s).GenerateAsync(s.RunId)); AssertExpectedRace(results); Assert.InRange(await s.Db.StatutoryFilingPackages.CountAsync(), 0, 1); }
    [Fact] public async Task Validate_vs_regenerate() { await using var s = await FilingScenario.CreateAsync(); Assert.True((await Service(s).GenerateAsync(s.RunId)).Succeeded); var results = await Race(s, () => Service(s).ValidateAsync(s.RunId), () => Service(s).GenerateAsync(s.RunId)); AssertExpectedRace(results); Assert.InRange(await s.Db.StatutoryFilingPackages.CountAsync(), 0, 1); }
    [Fact] public async Task Submit_for_approval_vs_regenerate() { await using var s = await FilingScenario.CreateAsync(); Assert.True((await Service(s).GenerateAsync(s.RunId)).Succeeded); Assert.True((await Service(s).ValidateAsync(s.RunId)).Succeeded); var results = await Race(s, () => Service(s).SubmitForApprovalAsync(s.RunId), () => Service(s).GenerateAsync(s.RunId)); AssertExpectedRace(results); Assert.DoesNotContain(await s.Db.StatutoryFilingRuns.Select(x => x.Status).ToListAsync(), x => x == StatutoryFilingRunStatus.Submitting); }
    [Fact] public async Task Approve_vs_cancel() { await using var s = await FilingScenario.CreateAsync(); await PrepareForApproval(s); var results = await Race(s, () => Service(s, s.CheckerId).ApproveAsync(s.RunId), () => Service(s).CancelAsync(s.RunId)); AssertExpectedRace(results); Assert.InRange(await s.Db.StatutoryFilingHistories.CountAsync(x => x.RunId == s.RunId && (x.Event == StatutoryFilingHistoryEvent.Approved || x.Event == StatutoryFilingHistoryEvent.Cancelled)), 0, 1); }
    [Fact] public async Task Approve_vs_approve() { await using var s = await FilingScenario.CreateAsync(); await PrepareForApproval(s); var results = await Race(s, () => Service(s, s.CheckerId).ApproveAsync(s.RunId), () => Service(s, s.CheckerId).ApproveAsync(s.RunId)); AssertExpectedRace(results); Assert.InRange(await s.Db.StatutoryFilingHistories.CountAsync(x => x.RunId == s.RunId && x.Event == StatutoryFilingHistoryEvent.Approved), 0, 1); }
    [Fact] public async Task Submit_vs_submit() { await using var s = await FilingScenario.CreateAsync(); await PrepareForApproval(s); Assert.True((await Service(s, s.CheckerId).ApproveAsync(s.RunId)).Succeeded); var results = await Race(s, () => Service(s, s.CheckerId).SubmitAsync(s.RunId), () => Service(s, s.CheckerId).SubmitAsync(s.RunId)); AssertExpectedRace(results); Assert.InRange(await s.Db.StatutoryFilingSubmissions.CountAsync(x => x.RunId == s.RunId), 0, 1); }
    [Fact] public async Task Acknowledge_vs_cancel() { await using var s = await FilingScenario.CreateAsync(); await PrepareForApproval(s); Assert.True((await Service(s, s.CheckerId).ApproveAsync(s.RunId)).Succeeded); Assert.True((await Service(s, s.CheckerId).SubmitAsync(s.RunId)).Succeeded); var results = await Race(s, () => Service(s, s.CheckerId).AcknowledgeAsync(s.RunId, new StatutoryFilingAcknowledgementRequest { ReferenceNumber = "ACK-1" }), () => Service(s).CancelAsync(s.RunId)); AssertExpectedRace(results); Assert.InRange(await s.Db.StatutoryFilingAcknowledgements.CountAsync(x => x.Submission!.RunId == s.RunId), 0, 1); }

    private static StatutoryFilingService Service(FilingScenario s, Guid? user = null) { var actor = new TestTenantContext(s.Tenant.TenantId, user ?? s.MakerId); var context = s.CreateContext(actor); return new StatutoryFilingService(context, actor, TimeProvider.System, new PayrollApprovalGuard(context, actor)); }
    private static async Task PrepareForApproval(FilingScenario s) { Assert.True((await Service(s).GenerateAsync(s.RunId)).Succeeded); Assert.True((await Service(s).ValidateAsync(s.RunId)).Succeeded); Assert.True((await Service(s).SubmitForApprovalAsync(s.RunId)).Succeeded); }
    private static async Task<OperationResult[]> Race(FilingScenario s, Func<Task<Result<StatutoryFilingRunDto>>> left, Func<Task<Result<StatutoryFilingRunDto>>> right) { var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously); var a = Capture(gate.Task, left); var b = Capture(gate.Task, right); gate.SetResult(true); return await Task.WhenAll(a, b); }
    private static async Task<OperationResult> Capture(Task gate, Func<Task<Result<StatutoryFilingRunDto>>> operation) { await gate; try { return new(await operation(), null); } catch (Exception ex) { return new(null, ex); } }
    private static void AssertExpectedRace(IEnumerable<OperationResult> results) { foreach (var result in results) if (result.Exception is not null) Assert.True(result.Exception is DbUpdateConcurrencyException || result.Exception is InvalidOperationException || result.Exception is Microsoft.Data.Sqlite.SqliteException, result.Exception.ToString()); }
    private sealed record OperationResult(Result<StatutoryFilingRunDto>? Value, Exception? Exception);
}

internal sealed class FilingScenario : IAsyncDisposable
{
    private readonly SqliteInMemoryDatabase database;
    public HrmsDbContext Db { get; } = null!;
    public TestTenantContext Tenant { get; } = new();
    public Guid RunId { get; private set; }
    public Guid MakerId { get; private set; }
    public Guid CheckerId { get; private set; }
    private FilingScenario(SqliteInMemoryDatabase database, HrmsDbContext db) { this.database = database; Db = db; }
    public HrmsDbContext CreateContext() => database.CreateContext(Tenant);
    public HrmsDbContext CreateContext(ITenantContext tenant) => database.CreateContext(tenant);
    public static async Task<FilingScenario> CreateAsync()
    {
        var database = new SqliteInMemoryDatabase(); var tenant = new TestTenantContext(Guid.NewGuid(), Guid.NewGuid()); var db = database.CreateContext(tenant); var maker = tenant.UserId!.Value; var checker = Guid.NewGuid(); var employee = Guid.NewGuid(); var periodId = Guid.NewGuid(); var batchId = Guid.NewGuid();
        db.Tenants.Add(new Tenant { Id = tenant.TenantId!.Value, TenantCode = $"SC{tenant.TenantId:N}"[..12], TenantName = "Scenario", Host = $"{tenant.TenantId:N}.test", ShardKey = tenant.TenantId.Value.ToString("N"), Status = TenantStatus.Active }); db.Users.AddRange(new User { Id = maker, TenantId = tenant.TenantId.Value, Email = $"{maker:N}@test", PasswordHash = "hash", IsActive = true }, new User { Id = checker, TenantId = tenant.TenantId.Value, Email = $"{checker:N}@test", PasswordHash = "hash", IsActive = true }); db.PayrollControlConfigurations.Add(new PayrollControlConfiguration { Id = Guid.NewGuid(), TenantId = tenant.TenantId.Value, RequireMakerChecker = true, PreventSelfApproval = true, UpdatedAtUtc = DateTime.UtcNow, UpdatedByUserId = maker }); db.Employees.Add(new Employee { Id = employee, TenantId = tenant.TenantId.Value, EmployeeCode = "SC-001", FirstName = "Scenario", LastName = "Employee", Email = $"{employee:N}@test", DateOfJoining = new(2026, 1, 1), Status = EmployeeStatus.Active }); db.PayrollCompliancePeriods.Add(new PayrollCompliancePeriod { Id = periodId, TenantId = tenant.TenantId.Value, JurisdictionCode = "IN", ComplianceType = PayrollComplianceType.IncomeTaxTds, PeriodStart = new(2026, 9, 1), PeriodEnd = new(2026, 9, 30), Status = PayrollCompliancePeriodStatus.Open }); var batch = new PayrollStatutoryReturnBatch { Id = batchId, TenantId = tenant.TenantId.Value, PayrollCompliancePeriodId = periodId, ComplianceType = PayrollComplianceType.IncomeTaxTds, JurisdictionCode = "IN", BatchNumber = $"SC-{Guid.NewGuid():N}", Status = PayrollStatutoryReturnStatus.Approved }; batch.Employees.Add(new PayrollStatutoryReturnEmployee { Id = Guid.NewGuid(), TenantId = tenant.TenantId.Value, PayrollStatutoryReturnBatchId = batchId, EmployeeId = employee, EmployeeCodeSnapshot = "SC-001", EmployeeNameSnapshot = "Scenario Employee", PayableAmount = 100m, Sequence = 1 }); db.PayrollStatutoryReturnBatches.Add(batch); await db.SaveChangesAsync(); var service = new StatutoryFilingService(db, tenant, TimeProvider.System, new PayrollApprovalGuard(db, tenant)); var definition = await service.CreateDefinitionAsync(new StatutoryFilingDefinitionRequest { Code = "SC-FILING", Name = "Scenario filing", FilingType = "Generic", JurisdictionCode = "IN" }); var run = await service.CreateRunAsync(new StatutoryFilingRunRequest { DefinitionId = definition.Value!.Id, FilingPeriod = "2026-09" }); var scenario = new FilingScenario(database, db) { RunId = run.Value!.Id, MakerId = maker, CheckerId = checker }; scenario.Tenant.TenantId = tenant.TenantId; scenario.Tenant.UserId = maker; return scenario;
    }
    public async ValueTask DisposeAsync() { await Db.DisposeAsync(); database.Dispose(); }
}
