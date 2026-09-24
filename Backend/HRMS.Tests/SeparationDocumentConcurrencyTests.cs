using System.Data.Common;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Separation;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Entities.Separation;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

[CollectionDefinition("Separation document SQLite isolation", DisableParallelization = true)]
public sealed class SeparationDocumentSqliteIsolationCollectionDefinition;

[Collection("Separation document SQLite isolation")]
public sealed class SeparationDocumentConcurrencyTests
{
    [Fact]
    public async Task Number_uniqueness_across_multiple_employees_in_same_tenant()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid(); var reasonId = Guid.NewGuid(); var templateId = Guid.NewGuid(); var versionId = Guid.NewGuid();
        var employees = Enumerable.Range(1, 2).Select(i => new Employee { Id = Guid.NewGuid(), TenantId = tenantId, FirstName = "Number", LastName = $"User{i}", Email = $"number{i}@example.test", EmployeeCode = $"NUM-{i:000}", DateOfJoining = new(2020, 1, 1), Status = EmployeeStatus.Active }).ToList();
        var separations = employees.Select((employee, i) => new EmployeeSeparation { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employee.Id, ReasonId = reasonId, SeparationNumber = $"NUM-SEP-{i:000}", SeparationType = SeparationType.EmployeeInitiated, RequestDate = new(2026, 1, 1), ProposedLastWorkingDate = new(2026, 10, 2), ApprovedLastWorkingDate = new(2026, 10, 2), Status = EmployeeSeparationStatus.Approved }).ToList();
        await using (var seed = database.CreateIsolatedContext(new TestTenantContext(tenantId)))
        {
            seed.Tenants.Add(new Tenant { Id = tenantId, TenantCode = "NUM", TenantName = "Number Tenant", Address = "Number Address" });
            seed.SeparationReasons.Add(new HRMS.Domain.Entities.Separation.SeparationReason { Id = reasonId, TenantId = tenantId, Code = "R", Name = "Resignation", EffectiveFrom = new(2020, 1, 1) });
            seed.Employees.AddRange(employees); seed.EmployeeSeparations.AddRange(separations);
            seed.SeparationDocumentTemplates.Add(new SeparationDocumentTemplate { Id = templateId, TenantId = tenantId, Code = "NUM-RL", Name = "Numbers", DocumentType = SeparationDocumentType.RelievingLetter, EffectiveFrom = new(2020, 1, 1) });
            seed.SeparationDocumentTemplateVersions.Add(new SeparationDocumentTemplateVersion { Id = versionId, TenantId = tenantId, TemplateId = templateId, VersionNumber = 1, Status = SeparationDocumentTemplateVersionStatus.Published, EffectiveFrom = new(2020, 1, 1), BodyTemplate = "{{Employee.EmployeeCode}}|{{Document.Number}}" });
            foreach (var separation in separations)
            {
                seed.EmployeeEmploymentHistory.Add(new() { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = separation.EmployeeId, EffectiveFrom = new(2020, 1, 1), DesignationName = "Engineer", DepartmentName = "Technology", GradeLevel = "G4", EmploymentStatus = EmployeeStatus.Active });
                seed.SeparationClearances.Add(new() { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeSeparationId = separation.Id, EmployeeId = separation.EmployeeId, TemplateId = Guid.NewGuid(), Status = SeparationClearanceStatus.Completed, CompletedAtUtc = DateTime.UtcNow });
                var settlement = new FinalSettlementCase { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = separation.EmployeeId, SeparationDate = new(2026, 10, 2), LastWorkingDate = new(2026, 10, 2), SettlementDate = new(2026, 10, 2), Status = FinalSettlementStatus.Finalized, FinalizedAtUtc = DateTime.UtcNow };
                seed.FinalSettlementCases.Add(settlement); seed.SeparationSettlementOrchestrations.Add(new() { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeSeparationId = separation.Id, EmployeeId = separation.EmployeeId, PayrollFinalSettlementId = settlement.Id, Status = SeparationSettlementOrchestrationStatus.Completed, CompletedAtUtc = DateTime.UtcNow });
            }
            await seed.SaveChangesAsync();
        }
        var startGate = new Barrier(employees.Count);
        var results = await Task.WhenAll(separations.Select(separation => Task.Run(async () =>
        {
            for (var attempt = 0; attempt < 4; attempt++)
            {
                await using var db = database.CreateIsolatedContext(new TestTenantContext(tenantId, Guid.NewGuid()));
                if (attempt == 0) startGate.SignalAndWait();
                Result<SeparationGeneratedDocumentDto> result;
                try
                {
                    result = await new SeparationDocumentService(db, new TestTenantContext(tenantId, Guid.NewGuid()), TimeProvider.System, new EmployeeIdentityResolver(db, new TestTenantContext(tenantId, Guid.NewGuid()))).GenerateAsync(separation.Id, new() { DocumentType = SeparationDocumentType.RelievingLetter, TemplateVersionId = versionId });
                }
                catch (DbException) when (attempt < 3)
                {
                    await Task.Delay(50);
                    continue;
                }
                if (result.Succeeded || attempt == 3) return result;
                await Task.Delay(25);
            }
            throw new InvalidOperationException("Generation retry loop did not return a result.");
        })));
        for (var i = 0; i < results.Length; i++)
        {
            if (results[i].Succeeded) continue;
            await using var retryDb = database.CreateIsolatedContext(new TestTenantContext(tenantId));
            var retry = await new SeparationDocumentService(retryDb, new TestTenantContext(tenantId, Guid.NewGuid()), TimeProvider.System, new EmployeeIdentityResolver(retryDb, new TestTenantContext(tenantId, Guid.NewGuid()))).GenerateAsync(separations[i].Id, new() { DocumentType = SeparationDocumentType.RelievingLetter, TemplateVersionId = versionId });
            Assert.True(retry.Succeeded, retry.Message);
        }
        await using var verify = database.CreateIsolatedContext(new TestTenantContext(tenantId));
        var documents = await verify.SeparationGeneratedDocuments.AsNoTracking().Where(x => x.DocumentType == SeparationDocumentType.RelievingLetter).ToListAsync();
        Assert.Equal(employees.Count, documents.Count); Assert.Equal(employees.Count, documents.Select(x => x.DocumentNumber).Distinct(StringComparer.Ordinal).Count()); Assert.Equal(0, documents.GroupBy(x => x.DocumentNumber).Count(x => x.Count() > 1));
        var sequence = await verify.SeparationDocumentNumberSequences.SingleAsync(x => x.TenantId == tenantId && x.DocumentType == SeparationDocumentType.RelievingLetter); Assert.Equal(employees.Count + 1, sequence.NextNumber);
    }

    [Fact]
    public async Task Number_sequences_are_isolated_by_tenant_scope()
    {
        using var tenantADatabase = new SqliteInMemoryDatabase();
        using var tenantBDatabase = new SqliteInMemoryDatabase();
        var tenantA = await PrepareAsync(tenantADatabase, false, "NUM-TENANT-A");
        var tenantB = await PrepareAsync(tenantBDatabase, false, "NUM-TENANT-B");
        var results = await Task.WhenAll(
            Task.Run(async () => { await using var db = tenantADatabase.CreateContext(new TestTenantContext(tenantA.Fixture.TenantId, tenantA.Fixture.HrUserId)); return await Service(db, tenantA.Fixture).GenerateAsync(tenantA.Fixture.SeparationId, new() { DocumentType = SeparationDocumentType.RelievingLetter, TemplateVersionId = tenantA.VersionId }); }),
            Task.Run(async () => { await using var db = tenantBDatabase.CreateContext(new TestTenantContext(tenantB.Fixture.TenantId, tenantB.Fixture.HrUserId)); return await Service(db, tenantB.Fixture).GenerateAsync(tenantB.Fixture.SeparationId, new() { DocumentType = SeparationDocumentType.RelievingLetter, TemplateVersionId = tenantB.VersionId }); }));
        Assert.All(results, result => Assert.True(result.Succeeded, result.Message));
        Assert.Equal(results[0].Value!.DocumentNumber, results[1].Value!.DocumentNumber);
        await using var verifyA = tenantADatabase.CreateContext(new TestTenantContext(tenantA.Fixture.TenantId));
        await using var verifyB = tenantBDatabase.CreateContext(new TestTenantContext(tenantB.Fixture.TenantId));
        var sequenceA = await verifyA.SeparationDocumentNumberSequences.SingleAsync(x => x.TenantId == tenantA.Fixture.TenantId && x.DocumentType == SeparationDocumentType.RelievingLetter);
        var sequenceB = await verifyB.SeparationDocumentNumberSequences.SingleAsync(x => x.TenantId == tenantB.Fixture.TenantId && x.DocumentType == SeparationDocumentType.RelievingLetter);
        Assert.Equal(2, sequenceA.NextNumber);
        Assert.Equal(2, sequenceB.NextNumber);
    }

    [Fact]
    public async Task Generate_vs_generate_same_type()
    {
        using var database = new SqliteInMemoryDatabase();
        var scenario = await PrepareAsync(database, false, "CON-GEN");
        var gate = new Barrier(2);
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(async () =>
        {
            await using var db = database.CreateIsolatedContext(new TestTenantContext(scenario.Fixture.TenantId, scenario.Fixture.HrUserId));
            gate.SignalAndWait();
            return await Service(db, scenario.Fixture).GenerateAsync(scenario.Fixture.SeparationId, new() { DocumentType = SeparationDocumentType.RelievingLetter, TemplateVersionId = scenario.VersionId });
        })));
        await using var verify = database.CreateIsolatedContext(new TestTenantContext(scenario.Fixture.TenantId, scenario.Fixture.HrUserId));
        var currentDocuments = await verify.SeparationGeneratedDocuments.Where(x => x.EmployeeSeparationId == scenario.Fixture.SeparationId && x.DocumentType == SeparationDocumentType.RelievingLetter && x.Status != SeparationGeneratedDocumentStatus.Superseded).ToListAsync();
        Assert.All(currentDocuments, document => Assert.Equal("CURRENT", document.CurrentDocumentKey));
        Assert.Single(currentDocuments);
        Assert.All(results, result => Assert.True(result.Succeeded, result.Message));
        Assert.Equal(0, await verify.SeparationGeneratedDocuments.GroupBy(x => x.DocumentNumber).Where(x => x.Count() > 1).CountAsync());
    }

    [Fact]
    public async Task Approve_vs_supersede()
    {
        using var database = new SqliteInMemoryDatabase();
        var scenario = await PrepareAsync(database, true, "CON-APP-SUP");
        await using var seedDb = database.CreateIsolatedContext(new TestTenantContext(scenario.Fixture.TenantId, scenario.Fixture.HrUserId));
        var generated = await Service(seedDb, scenario.Fixture).GenerateAsync(scenario.Fixture.SeparationId, new() { DocumentType = SeparationDocumentType.RelievingLetter, TemplateVersionId = scenario.VersionId });
        Assert.True(generated.Succeeded, generated.Message);
        var gate = new Barrier(2);
        var results = await Task.WhenAll(
            Task.Run(async () => { await using var db = database.CreateIsolatedContext(new TestTenantContext(scenario.Fixture.TenantId, Guid.NewGuid())); gate.SignalAndWait(); return await Service(db, scenario.Fixture, Guid.NewGuid()).ApproveAsync(generated.Value!.Id, new()); }),
            Task.Run(async () => { await using var db = database.CreateIsolatedContext(new TestTenantContext(scenario.Fixture.TenantId, scenario.Fixture.HrUserId)); gate.SignalAndWait(); return await Service(db, scenario.Fixture).SupersedeAsync(generated.Value!.Id, new() { Reason = "Concurrent correction" }); }));
        Assert.True(results.Any(x => x.Succeeded));
        await using var verify = database.CreateIsolatedContext(new TestTenantContext(scenario.Fixture.TenantId, scenario.Fixture.HrUserId));
        var old = await verify.SeparationGeneratedDocuments.SingleAsync(x => x.Id == generated.Value.Id);
        Assert.Contains(old.Status, new[] { SeparationGeneratedDocumentStatus.Superseded, SeparationGeneratedDocumentStatus.Approved, SeparationGeneratedDocumentStatus.PendingApproval });
    }

    [Fact]
    public async Task Approve_vs_cancel()
    {
        using var database = new SqliteInMemoryDatabase();
        var scenario = await PrepareAsync(database, true, "CON-APP-CAN");
        await using var seedDb = database.CreateIsolatedContext(new TestTenantContext(scenario.Fixture.TenantId, scenario.Fixture.HrUserId));
        var generated = await Service(seedDb, scenario.Fixture).GenerateAsync(scenario.Fixture.SeparationId, new() { DocumentType = SeparationDocumentType.RelievingLetter, TemplateVersionId = scenario.VersionId });
        Assert.True(generated.Succeeded, generated.Message);
        var results = await Task.WhenAll(
            Task.Run(async () => { await using var db = database.CreateIsolatedContext(new TestTenantContext(scenario.Fixture.TenantId, Guid.NewGuid())); return await Service(db, scenario.Fixture, Guid.NewGuid()).ApproveAsync(generated.Value!.Id, new()); }),
            Task.Run(async () => { await using var db = database.CreateIsolatedContext(new TestTenantContext(scenario.Fixture.TenantId, scenario.Fixture.HrUserId)); return await Service(db, scenario.Fixture).CancelAsync(generated.Value!.Id, new() { Reason = "Concurrent cancellation" }); }));
        Assert.True(results.Any(x => x.Succeeded));
        await using var verify = database.CreateIsolatedContext(new TestTenantContext(scenario.Fixture.TenantId, scenario.Fixture.HrUserId));
        var status = await verify.SeparationGeneratedDocuments.Where(x => x.Id == generated.Value.Id).Select(x => x.Status).SingleAsync();
        Assert.Contains(status, new[] { SeparationGeneratedDocumentStatus.Approved, SeparationGeneratedDocumentStatus.Cancelled });
    }

    [Fact]
    public async Task Supersede_vs_supersede()
    {
        using var database = new SqliteInMemoryDatabase();
        var scenario = await PrepareAsync(database, false, "CON-SUP");
        await using var seedDb = database.CreateIsolatedContext(new TestTenantContext(scenario.Fixture.TenantId, scenario.Fixture.HrUserId));
        var generated = await Service(seedDb, scenario.Fixture).GenerateAsync(scenario.Fixture.SeparationId, new() { DocumentType = SeparationDocumentType.RelievingLetter, TemplateVersionId = scenario.VersionId });
        Assert.True(generated.Succeeded, generated.Message);
        var gate = new Barrier(2);
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(async () => { await using var db = database.CreateIsolatedContext(new TestTenantContext(scenario.Fixture.TenantId, scenario.Fixture.HrUserId)); gate.SignalAndWait(); return await Service(db, scenario.Fixture).SupersedeAsync(generated.Value!.Id, new() { Reason = "Concurrent correction" }); })));
        await using var verify = database.CreateIsolatedContext(new TestTenantContext(scenario.Fixture.TenantId, scenario.Fixture.HrUserId));
        var current = await verify.SeparationGeneratedDocuments.CountAsync(x => x.EmployeeSeparationId == scenario.Fixture.SeparationId && x.DocumentType == SeparationDocumentType.RelievingLetter && new[] { SeparationGeneratedDocumentStatus.Issued, SeparationGeneratedDocumentStatus.Approved, SeparationGeneratedDocumentStatus.PendingApproval }.Contains(x.Status));
        Assert.Equal(1, current);
        Assert.True(results.Count(x => x.Succeeded) <= 1);
    }

    [Fact]
    public async Task Template_publish_vs_template_publish()
    {
        using var database = new SqliteInMemoryDatabase();
        var fixture = await NoticeTestData.CreateApprovedAsync(database, new(2026, 10, 2));
        await using var db = database.CreateIsolatedContext(new TestTenantContext(fixture.TenantId, fixture.HrUserId));
        var service = Service(db, fixture);
        var template = await service.CreateTemplateAsync(new() { Code = "CON-PUB", Name = "Concurrent", DocumentType = SeparationDocumentType.RelievingLetter });
        var one = await service.AddVersionAsync(template.Value!.Id, new() { BodyTemplate = "One" });
        var two = await service.AddVersionAsync(template.Value.Id, new() { BodyTemplate = "Two", EffectiveFrom = new(2027, 1, 1) });
        var gate = new Barrier(2);
        var results = await Task.WhenAll(new[] { one.Value!.Versions.Last().Id, two.Value!.Versions.Last().Id }.Select(id => Task.Run(async () => { await using var context = database.CreateIsolatedContext(new TestTenantContext(fixture.TenantId, fixture.HrUserId)); gate.SignalAndWait(); return await Service(context, fixture).PublishVersionAsync(template.Value.Id, id, 1); })));
        Assert.True(results.Count(x => x.Succeeded) <= 1);
    }

    [Fact]
    public async Task Generate_vs_lwd_revision()
    {
        using var database = new SqliteInMemoryDatabase();
        var scenario = await PrepareAsync(database, false, "CON-LWD");
        var gate = new Barrier(2);
        var generationTask = Task.Run(async () => { await using var db = database.CreateIsolatedContext(new TestTenantContext(scenario.Fixture.TenantId, scenario.Fixture.HrUserId)); gate.SignalAndWait(); return await Service(db, scenario.Fixture).GenerateAsync(scenario.Fixture.SeparationId, new() { DocumentType = SeparationDocumentType.RelievingLetter, TemplateVersionId = scenario.VersionId }); });
        var revisionTask = Task.Run(async () => { await using var db = database.CreateIsolatedContext(new TestTenantContext(scenario.Fixture.TenantId, scenario.Fixture.HrUserId)); gate.SignalAndWait(); return await NoticeTestData.CreateService(db, scenario.Fixture.TenantId, scenario.Fixture.HrUserId).ReviseApprovedLwdAsync(scenario.Fixture.SeparationId, new(new(2026, 10, 10), "Concurrent correction")); });
        await Task.WhenAll(generationTask, revisionTask);
        Assert.True(generationTask.Result.Succeeded || revisionTask.Result.Succeeded);
        await using var verify = database.CreateIsolatedContext(new TestTenantContext(scenario.Fixture.TenantId, scenario.Fixture.HrUserId));
        var document = await verify.SeparationGeneratedDocuments.SingleOrDefaultAsync();
        if (document is not null) Assert.Contains("2026-10-02", document.SnapshotJson, StringComparison.Ordinal);
    }

    private static SeparationDocumentService Service(HrmsDbContext db, NoticeFixture fixture, Guid? userId = null)
    {
        var tenant = new TestTenantContext(fixture.TenantId, userId ?? fixture.HrUserId);
        return new(db, tenant, TimeProvider.System, new EmployeeIdentityResolver(db, tenant));
    }

    private static async Task<(NoticeFixture Fixture, Guid VersionId)> PrepareAsync(SqliteInMemoryDatabase database, bool approval, string code)
    {
        var fixture = await NoticeTestData.CreateApprovedAsync(database, new(2026, 10, 2));
        await using (var seed = database.CreateContext(new TestTenantContext(fixture.TenantId, fixture.HrUserId)))
        {
            seed.EmployeeEmploymentHistory.RemoveRange(await seed.EmployeeEmploymentHistory.Where(x => x.EmployeeId == fixture.EmployeeId).ToListAsync());
            seed.EmployeeEmploymentHistory.Add(new() { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, EffectiveFrom = new(2020, 1, 1), DesignationName = "Historical Engineer", DepartmentName = "Historical Technology", GradeLevel = "G4", EmploymentStatus = EmployeeStatus.Active });
            seed.SeparationClearances.Add(new() { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeSeparationId = fixture.SeparationId, EmployeeId = fixture.EmployeeId, TemplateId = Guid.NewGuid(), Status = SeparationClearanceStatus.Completed, CompletedAtUtc = DateTime.UtcNow });
            var settlement = new FinalSettlementCase { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, SeparationDate = new(2026, 10, 2), LastWorkingDate = new(2026, 10, 2), SettlementDate = new(2026, 10, 2), Status = FinalSettlementStatus.Finalized, FinalizedAtUtc = DateTime.UtcNow };
            seed.FinalSettlementCases.Add(settlement); seed.SeparationSettlementOrchestrations.Add(new() { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeSeparationId = fixture.SeparationId, EmployeeId = fixture.EmployeeId, PayrollFinalSettlementId = settlement.Id, Status = SeparationSettlementOrchestrationStatus.Completed, CompletedAtUtc = DateTime.UtcNow }); await seed.SaveChangesAsync();
        }
        await using var db = database.CreateContext(new TestTenantContext(fixture.TenantId, fixture.HrUserId));
        var service = Service(db, fixture); var template = await service.CreateTemplateAsync(new() { Code = code, Name = code, DocumentType = SeparationDocumentType.RelievingLetter, RequiresApproval = approval }); var version = await service.AddVersionAsync(template.Value!.Id, new() { BodyTemplate = "{{Employee.FullName}}|{{Separation.ApprovedLastWorkingDate}}|{{Document.Number}}" }); await service.PublishVersionAsync(template.Value.Id, version.Value!.Versions.Single().Id, 1); return (fixture, version.Value.Versions.Single().Id);
    }
}
