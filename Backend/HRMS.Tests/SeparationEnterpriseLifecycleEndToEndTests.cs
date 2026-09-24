using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Entities.Separation;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class SeparationEnterpriseLifecycleEndToEndTests
{
    [Fact]
    public async Task Full_separation_lifecycle_from_active_employee_to_closed_exit()
    {
        using var database = new SqliteInMemoryDatabase();
        var lwd = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var setup = await ExitInterviewTestData.CreateAsync(database, lwd);

        await ExitInterviewTestData.SaveDraftAsync(database, setup);
        await ExitInterviewTestData.SubmitAsync(database, setup);
        await ExitInterviewTestData.CompleteAsync(database, setup);

        await using (var clearanceDb = database.CreateContext(new TestTenantContext(setup.TenantId, setup.HrUserId)))
        {
            var tenant = new TestTenantContext(setup.TenantId, setup.HrUserId);
            var clearance = new ClearanceService(clearanceDb, tenant, new EmployeeIdentityResolver(clearanceDb, tenant), new EmployeeManagerResolver(clearanceDb, tenant), TimeProvider.System);
            var template = await clearance.CreateTemplateAsync(new(
                "E2E-CLEARANCE", "E2E Clearance", null, new(2020, 1, 1), null, null, null,
                [new("HR-CHECK", "HR check", null, SeparationClearanceTaskCategory.Hr, ClearanceOwnerType.Hr, null, false, false, false, false, 1, null)]));
            Assert.True(template.Succeeded, template.Message);
            var started = await clearance.StartAsync(setup.SeparationId);
            Assert.True(started.Succeeded, started.Message);
            var completed = await clearance.CompleteAsync(started.Value!.Id);
            Assert.True(completed.Succeeded, completed.Message);
        }

        Guid settlementId;
        await using (var settlementDb = database.CreateContext(new TestTenantContext(setup.TenantId, setup.HrUserId)))
        {
            var tenant = new TestTenantContext(setup.TenantId, setup.HrUserId);
            var payroll = new PayrollRetroSettlementService(settlementDb, tenant, TimeProvider.System);
            var orchestration = new SeparationSettlementOrchestrationService(settlementDb, tenant, payroll, TimeProvider.System);
            var initiated = await orchestration.InitiateAsync(setup.SeparationId, new() { IdempotencyKey = "full-e2e-settlement" });
            Assert.True(initiated.Succeeded, initiated.Message);
            Console.WriteLine($"Phase 8F initiation: status={initiated.Value?.OrchestrationStatus}, settlement={initiated.Value?.PayrollFinalSettlementId}, message={initiated.Message}, cases={await settlementDb.FinalSettlementCases.CountAsync()}, orchestrations={await settlementDb.SeparationSettlementOrchestrations.CountAsync()}");
            settlementId = await settlementDb.SeparationSettlementOrchestrations
                .Where(x => x.EmployeeSeparationId == setup.SeparationId)
                .Select(x => x.PayrollFinalSettlementId)
                .SingleAsync() ?? throw new InvalidOperationException("Phase 8F did not link a Final Settlement.");
            Assert.True((await payroll.CalculateSettlementAsync(settlementId)).Succeeded);
            Assert.True((await payroll.ApproveSettlementAsync(settlementId)).Succeeded);
            Assert.True((await payroll.FinalizeSettlementAsync(settlementId)).Succeeded);
            var status = await orchestration.GetStatusAsync(setup.SeparationId);
            Assert.True(status.Succeeded, status.Message);
            Assert.True(status.Value!.ReadyForFinalExitClosure);
        }

        Guid documentId;
        string documentNumber;
        string contentHash;
        string snapshot;
        await using (var documentDb = database.CreateContext(new TestTenantContext(setup.TenantId, setup.HrUserId)))
        {
            var tenant = new TestTenantContext(setup.TenantId, setup.HrUserId);
            var documents = new SeparationDocumentService(documentDb, tenant, TimeProvider.System, new EmployeeIdentityResolver(documentDb, tenant));
            var template = await documents.CreateTemplateAsync(new() { Code = "E2E-RL", Name = "E2E Relieving", DocumentType = SeparationDocumentType.RelievingLetter, RequiresApproval = false });
            Assert.True(template.Succeeded, template.Message);
            var version = await documents.AddVersionAsync(template.Value!.Id, new() { BodyTemplate = "{{Employee.FullName}}|{{Employee.EmployeeCode}}|{{Separation.ApprovedLastWorkingDate}}|{{Document.Number}}" });
            Assert.True(version.Succeeded, version.Message);
            Assert.True((await documents.PublishVersionAsync(template.Value.Id, version.Value!.Versions.Single().Id, 1)).Succeeded);
            var generated = await documents.GenerateAsync(setup.SeparationId, new() { DocumentType = SeparationDocumentType.RelievingLetter });
            Assert.True(generated.Succeeded, generated.Message);
            documentId = generated.Value!.Id;
            documentNumber = generated.Value.DocumentNumber;
            contentHash = generated.Value.ContentHash;
            snapshot = await documentDb.SeparationGeneratedDocuments.Where(x => x.Id == documentId).Select(x => x.SnapshotJson).SingleAsync();
        }

        await using (var verifyReady = database.CreateIsolatedContext(new TestTenantContext(setup.TenantId, setup.HrUserId)))
        {
            var readiness = await new SeparationExitService(verifyReady, new TestTenantContext(setup.TenantId, setup.HrUserId), TimeProvider.System).GetReadinessAsync(setup.SeparationId);
            Assert.True(readiness.Succeeded, readiness.Message);
            Assert.True(readiness.Value!.IsReady, string.Join("; ", readiness.Value.Blockers.Select(x => $"{x.Code}:{x.Message}")));
        }

        await using (var exitDb = database.CreateContext(new TestTenantContext(setup.TenantId, setup.HrUserId)))
        {
            var result = await new SeparationExitService(exitDb, new TestTenantContext(setup.TenantId, setup.HrUserId), TimeProvider.System).ExecuteAsync(setup.SeparationId, new());
            Assert.True(result.Succeeded, result.Message);
        }

        await using var verify = database.CreateIsolatedContext(new TestTenantContext(setup.TenantId, setup.HrUserId));
        var employee = await verify.Employees.SingleAsync(x => x.Id == setup.EmployeeId);
        var separation = await verify.EmployeeSeparations.SingleAsync(x => x.Id == setup.SeparationId);
        var settlement = await verify.FinalSettlementCases.SingleAsync(x => x.Id == settlementId);
        var document = await verify.SeparationGeneratedDocuments.SingleAsync(x => x.Id == documentId);
        Assert.Equal(lwd, employee.DateOfLeaving);
        Assert.Equal(EmployeeStatus.Terminated, employee.Status);
        Assert.False((await verify.Users.SingleAsync(x => x.Id == setup.EmployeeUserId)).IsActive);
        Assert.Equal(FinalSettlementStatus.Finalized, settlement.Status);
        Assert.Equal(documentNumber, document.DocumentNumber);
        Assert.Equal(contentHash, document.ContentHash);
        Assert.Equal(snapshot, document.SnapshotJson);
        Assert.Equal(EmployeeSeparationStatus.Closed, separation.Status);
        Assert.Equal(1, await verify.SeparationExitExecutionEvents.CountAsync(x => x.EventType == SeparationExitExecutionEventType.SeparationClosed));
        Assert.Equal(1, await verify.EmployeeEmploymentHistory.CountAsync(x => x.EmployeeId == setup.EmployeeId && x.EmploymentStatus == EmployeeStatus.Terminated));

        await using var retryDb = database.CreateIsolatedContext(new TestTenantContext(setup.TenantId, setup.HrUserId));
        var retry = await new SeparationExitService(retryDb, new TestTenantContext(setup.TenantId, setup.HrUserId), TimeProvider.System).ExecuteAsync(setup.SeparationId, new());
        Assert.True(retry.Succeeded, retry.Message);
        Assert.Equal(1, await retryDb.SeparationExitExecutionEvents.CountAsync(x => x.EventType == SeparationExitExecutionEventType.SeparationClosed));
    }

    [Fact]
    public async Task Ready_separation_executes_terminal_exit_without_payroll_mutation()
    {
        using var database = new SqliteInMemoryDatabase();
        var fixture = await SeparationExitExecutionTests.ReadyFixtureAsync(database);
        await using var db = database.CreateContext(new TestTenantContext(fixture.TenantId, fixture.HrUserId));
        var beforePayroll = await db.PayrollResults.CountAsync(x => x.EmployeeId == fixture.EmployeeId);
        var result = await new SeparationExitService(db, new TestTenantContext(fixture.TenantId, fixture.HrUserId), TimeProvider.System).ExecuteAsync(fixture.SeparationId, new());
        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(EmployeeStatus.Terminated, (await db.Employees.SingleAsync(x => x.Id == fixture.EmployeeId)).Status);
        Assert.Equal(EmployeeSeparationStatus.Closed, (await db.EmployeeSeparations.SingleAsync(x => x.Id == fixture.SeparationId)).Status);
        Assert.Equal(beforePayroll, await db.PayrollResults.CountAsync(x => x.EmployeeId == fixture.EmployeeId));
        Assert.Equal(1, await db.SeparationExitExecutionEvents.CountAsync(x => x.EventType == HRMS.Domain.Entities.Separation.SeparationExitExecutionEventType.SeparationClosed));
    }
}
