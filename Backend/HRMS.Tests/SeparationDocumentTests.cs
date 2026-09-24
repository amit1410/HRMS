using HRMS.Application.Services;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Separation;
using HRMS.Domain.Entities.Separation;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace HRMS.Tests;

public sealed class SeparationDocumentTests
{
    [Fact]
    public async Task Unknown_merge_field_is_rejected_before_publishing()
    {
        using var database = new SqliteInMemoryDatabase();
        var fixture = await NoticeTestData.CreateApprovedAsync(database, new(2026, 10, 2));
        await using var db = database.CreateContext(new TestTenantContext(fixture.TenantId, fixture.HrUserId));
        var service = Service(db, fixture);
        var template = await service.CreateTemplateAsync(new() { Code = "RL", Name = "Relieving", DocumentType = SeparationDocumentType.RelievingLetter });
        Assert.True(template.Succeeded, template.Message);
        var version = await service.AddVersionAsync(template.Value!.Id, new() { BodyTemplate = "{{Employee.Secret}}" });
        Assert.False(version.Succeeded);
        Assert.Equal(ResultStatus.ValidationFailed, version.Status);
    }

    [Fact]
    public async Task Published_template_version_is_immutable_and_effective_dated()
    {
        using var database = new SqliteInMemoryDatabase();
        var fixture = await NoticeTestData.CreateApprovedAsync(database, new(2026, 10, 2));
        await using var db = database.CreateContext(new TestTenantContext(fixture.TenantId, fixture.HrUserId));
        var service = Service(db, fixture);
        var template = await service.CreateTemplateAsync(new() { Code = "EL", Name = "Experience", DocumentType = SeparationDocumentType.ExperienceLetter, EffectiveFrom = new(2026, 1, 1) });
        var added = await service.AddVersionAsync(template.Value!.Id, new() { BodyTemplate = "{{Employee.FullName}} {{Employment.LastDesignation}}", EffectiveFrom = new(2026, 1, 1) });
        var published = await service.PublishVersionAsync(template.Value.Id, added.Value!.Versions.Single().Id, 1);
        Assert.True(published.Succeeded, published.Message);
        Assert.Equal(SeparationDocumentTemplateVersionStatus.Published, published.Value!.Versions.Single().Status);
        Assert.Equal("{{Employee.FullName}} {{Employment.LastDesignation}}", published.Value.Versions.Single().BodyTemplate);
    }

    [Fact]
    public async Task Readiness_requires_finalized_settlement_and_clearance()
    {
        using var database = new SqliteInMemoryDatabase();
        var fixture = await NoticeTestData.CreateApprovedAsync(database, new(2026, 10, 2));
        await using var db = database.CreateContext(new TestTenantContext(fixture.TenantId, fixture.HrUserId));
        var result = await Service(db, fixture).GetReadinessAsync(fixture.SeparationId);
        Assert.True(result.Succeeded, result.Message);
        Assert.Contains("FinalSettlementNotFinalized", result.Value!.Blockers);
        Assert.Contains("ClearanceIncomplete", result.Value.Blockers);
    }

    [Fact]
    public async Task New_document_tables_are_tenant_scoped_and_model_is_present()
    {
        using var database = new SqliteInMemoryDatabase();
        await using var db = database.CreateContext(new TestTenantContext(Guid.NewGuid()));
        Assert.NotNull(db.Model.FindEntityType(typeof(SeparationDocumentTemplate)));
        Assert.NotNull(db.Model.FindEntityType(typeof(SeparationDocumentTemplateVersion)));
        Assert.NotNull(db.Model.FindEntityType(typeof(SeparationGeneratedDocument)));
        Assert.NotNull(db.Model.FindEntityType(typeof(SeparationDocumentEvent)));
        Assert.NotNull(db.Model.FindEntityType(typeof(SeparationDocumentNumberSequence)));
    }

    [Fact]
    public async Task Generation_persists_pdf_hash_and_historical_snapshot()
    {
        using var database = new SqliteInMemoryDatabase();
        var fixture = await NoticeTestData.CreateApprovedAsync(database, new(2026, 10, 2));
        await using (var seed = database.CreateContext(new TestTenantContext(fixture.TenantId, fixture.HrUserId)))
        {
            var existingHistory = await seed.EmployeeEmploymentHistory.Where(x => x.EmployeeId == fixture.EmployeeId).ToListAsync();
            seed.EmployeeEmploymentHistory.RemoveRange(existingHistory);
            seed.EmployeeEmploymentHistory.Add(new() { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, EffectiveFrom = new(2020, 1, 1), DesignationName = "Historical Engineer", DepartmentName = "Historical Technology", GradeLevel = "G4", EmploymentStatus = EmployeeStatus.Active });
            seed.SeparationClearances.Add(new() { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeSeparationId = fixture.SeparationId, EmployeeId = fixture.EmployeeId, TemplateId = Guid.NewGuid(), Status = SeparationClearanceStatus.Completed, CompletedAtUtc = DateTime.UtcNow });
            var settlement = new FinalSettlementCase { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, SeparationDate = new(2026, 10, 2), LastWorkingDate = new(2026, 10, 2), SettlementDate = new(2026, 10, 2), Status = FinalSettlementStatus.Finalized, FinalizedAtUtc = DateTime.UtcNow };
            seed.FinalSettlementCases.Add(settlement); seed.SeparationSettlementOrchestrations.Add(new() { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeSeparationId = fixture.SeparationId, EmployeeId = fixture.EmployeeId, PayrollFinalSettlementId = settlement.Id, Status = SeparationSettlementOrchestrationStatus.Completed, CompletedAtUtc = DateTime.UtcNow }); await seed.SaveChangesAsync();
        }
        await using var db = database.CreateContext(new TestTenantContext(fixture.TenantId, fixture.HrUserId)); var service = Service(db, fixture);
        var template = await service.CreateTemplateAsync(new() { Code = "RL", Name = "Relieving", DocumentType = SeparationDocumentType.RelievingLetter, EffectiveFrom = new(2026, 1, 1) }); var version = await service.AddVersionAsync(template.Value!.Id, new() { BodyTemplate = "{{Employee.FullName}}|{{Employment.LastDesignation}}|{{Separation.ApprovedLastWorkingDate}}|{{Document.Number}}", EffectiveFrom = new(2026, 1, 1) }); await service.PublishVersionAsync(template.Value.Id, version.Value!.Versions.Single().Id, 1);
        var generated = await service.GenerateAsync(fixture.SeparationId, new() { DocumentType = SeparationDocumentType.RelievingLetter });
        Assert.True(generated.Succeeded, generated.Message); var persisted = await db.SeparationGeneratedDocuments.SingleAsync(); var bytes = Convert.FromBase64String(persisted.ContentBase64); Assert.Equal(persisted.ContentHash, Convert.ToHexString(SHA256.HashData(bytes))); Assert.Contains("Historical Engineer", persisted.SnapshotJson); Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(bytes));
    }

    [Fact]
    public async Task Preview_returns_non_official_content_without_allocating_number_or_record()
    {
        using var database = new SqliteInMemoryDatabase();
        var fixture = await NoticeTestData.CreateApprovedAsync(database, new(2026, 10, 2));
        await using var db = database.CreateContext(new TestTenantContext(fixture.TenantId, fixture.HrUserId));
        var service = Service(db, fixture);
        var template = await service.CreateTemplateAsync(new() { Code = "PREVIEW", Name = "Preview", DocumentType = SeparationDocumentType.ExperienceLetter });
        var version = await service.AddVersionAsync(template.Value!.Id, new() { BodyTemplate = "{{Employee.FullName}}|{{Document.Number}}" });
        var preview = await service.PreviewAsync(version.Value!.Versions.Single().Id, new());
        Assert.True(preview.Succeeded, preview.Message);
        Assert.False(preview.Value!.IsOfficial);
        Assert.Contains("PREVIEW-NOT-OFFICIAL", System.Text.Encoding.ASCII.GetString(Convert.FromBase64String(preview.Value.ContentBase64)));
        Assert.Empty(await db.SeparationGeneratedDocuments.ToListAsync());
        Assert.Empty(await db.SeparationDocumentNumberSequences.ToListAsync());
    }

    [Fact]
    public async Task Experience_letter_uses_historical_facts_and_is_employee_visible_after_issue()
    {
        using var database = new SqliteInMemoryDatabase();
        var fixture = await NoticeTestData.CreateApprovedAsync(database, new(2026, 10, 2));
        await using (var seed = database.CreateContext(new TestTenantContext(fixture.TenantId, fixture.HrUserId)))
        {
            seed.EmployeeEmploymentHistory.RemoveRange(await seed.EmployeeEmploymentHistory.Where(x => x.EmployeeId == fixture.EmployeeId).ToListAsync());
            seed.EmployeeEmploymentHistory.Add(new() { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, EffectiveFrom = new(2020, 1, 1), DesignationName = "Historical Analyst", DepartmentName = "Historical Finance", GradeLevel = "G5", EmploymentStatus = EmployeeStatus.Active });
            seed.SeparationClearances.Add(new() { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeSeparationId = fixture.SeparationId, EmployeeId = fixture.EmployeeId, TemplateId = Guid.NewGuid(), Status = SeparationClearanceStatus.Completed, CompletedAtUtc = DateTime.UtcNow });
            var settlement = new FinalSettlementCase { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, SeparationDate = new(2026, 10, 2), LastWorkingDate = new(2026, 10, 2), SettlementDate = new(2026, 10, 2), Status = FinalSettlementStatus.Finalized, FinalizedAtUtc = DateTime.UtcNow };
            seed.FinalSettlementCases.Add(settlement); seed.SeparationSettlementOrchestrations.Add(new() { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeSeparationId = fixture.SeparationId, EmployeeId = fixture.EmployeeId, PayrollFinalSettlementId = settlement.Id, Status = SeparationSettlementOrchestrationStatus.Completed, CompletedAtUtc = DateTime.UtcNow }); await seed.SaveChangesAsync();
        }
        await using var hrDb = database.CreateContext(new TestTenantContext(fixture.TenantId, fixture.HrUserId)); var hr = Service(hrDb, fixture); var template = await hr.CreateTemplateAsync(new() { Code = "EL", Name = "Experience", DocumentType = SeparationDocumentType.ExperienceLetter, RequiresApproval = false }); var version = await hr.AddVersionAsync(template.Value!.Id, new() { BodyTemplate = "{{Employee.FullName}}|{{Employee.EmployeeCode}}|{{Employee.DateOfJoining}}|{{Employment.LastDesignation}}|{{Employment.LastDepartment}}|{{Separation.ApprovedLastWorkingDate}}|{{Organization.Name}}|{{Document.Number}}" }); await hr.PublishVersionAsync(template.Value.Id, version.Value!.Versions.Single().Id, 1); var generated = await hr.GenerateAsync(fixture.SeparationId, new() { DocumentType = SeparationDocumentType.ExperienceLetter });
        Assert.True(generated.Succeeded, generated.Message); var stored = await hrDb.SeparationGeneratedDocuments.SingleAsync(); Assert.Equal(SeparationDocumentType.ExperienceLetter, stored.DocumentType); Assert.Equal(version.Value.Versions.Single().Id, stored.TemplateVersionId); Assert.Contains("Historical Analyst", stored.SnapshotJson); Assert.Contains("Historical Finance", stored.SnapshotJson); Assert.Equal(stored.ContentHash, Convert.ToHexString(SHA256.HashData(Convert.FromBase64String(stored.ContentBase64))));
        await using var employeeDb = database.CreateContext(new TestTenantContext(fixture.TenantId, fixture.EmployeeUserId)); var employeeTenant = new TestTenantContext(fixture.TenantId, fixture.EmployeeUserId); var visible = await new SeparationDocumentService(employeeDb, employeeTenant, TimeProvider.System, new EmployeeIdentityResolver(employeeDb, employeeTenant)).GetForSeparationAsync(fixture.SeparationId, true); Assert.True(visible.Succeeded, visible.Message); Assert.Single(visible.Value!);
    }

    [Fact]
    public async Task Maker_checker_approval_and_issue_preserve_exact_artifact_and_gate_employee_visibility()
    {
        using var database = new SqliteInMemoryDatabase(); var fixture = await NoticeTestData.CreateApprovedAsync(database, new(2026, 10, 2)); await PrepareReadyAsync(database, fixture);
        await using var makerDb = database.CreateContext(new TestTenantContext(fixture.TenantId, fixture.HrUserId)); var maker = Service(makerDb, fixture); var template = await maker.CreateTemplateAsync(new() { Code = "RL-APP", Name = "Relieving approval", DocumentType = SeparationDocumentType.RelievingLetter, RequiresApproval = true }); var version = await maker.AddVersionAsync(template.Value!.Id, new() { BodyTemplate = "{{Employee.FullName}}|{{Document.Number}}" }); await maker.PublishVersionAsync(template.Value.Id, version.Value!.Versions.Single().Id, 1); var generated = await maker.GenerateAsync(fixture.SeparationId, new() { DocumentType = SeparationDocumentType.RelievingLetter }); Assert.True(generated.Succeeded, generated.Message); Assert.Equal(SeparationGeneratedDocumentStatus.PendingApproval, generated.Value!.Status); var hash = generated.Value.ContentHash; var number = generated.Value.DocumentNumber; var selfApproval = await maker.ApproveAsync(generated.Value.Id, new()); Assert.Equal(ResultStatus.Forbidden, selfApproval.Status);
        await using var checkerDb = database.CreateContext(new TestTenantContext(fixture.TenantId, Guid.NewGuid())); var checkerTenant = new TestTenantContext(fixture.TenantId, Guid.NewGuid()); var checker = new SeparationDocumentService(checkerDb, checkerTenant, TimeProvider.System, new EmployeeIdentityResolver(checkerDb, checkerTenant)); var approved = await checker.ApproveAsync(generated.Value.Id, new()); Assert.True(approved.Succeeded, approved.Message); Assert.Equal(hash, approved.Value!.ContentHash); Assert.Equal(number, approved.Value.DocumentNumber); await using var employeeDb = database.CreateContext(new TestTenantContext(fixture.TenantId, fixture.EmployeeUserId)); var employeeTenant = new TestTenantContext(fixture.TenantId, fixture.EmployeeUserId); var employee = new SeparationDocumentService(employeeDb, employeeTenant, TimeProvider.System, new EmployeeIdentityResolver(employeeDb, employeeTenant)); Assert.Empty((await employee.GetForSeparationAsync(fixture.SeparationId, true)).Value!); var issued = await checker.IssueAsync(generated.Value.Id, 0); Assert.True(issued.Succeeded, issued.Message); Assert.Single((await employee.GetForSeparationAsync(fixture.SeparationId, true)).Value!); Assert.Equal(1, await checkerDb.SeparationDocumentEvents.CountAsync(x => x.GeneratedDocumentId == generated.Value.Id && x.EventType == SeparationDocumentEventType.DocumentApproved));
    }

    [Fact]
    public async Task Supersede_preserves_original_artifact_and_creates_one_current_successor()
    {
        using var database = new SqliteInMemoryDatabase(); var fixture = await NoticeTestData.CreateApprovedAsync(database, new(2026, 10, 2)); await PrepareReadyAsync(database, fixture);
        await using var db = database.CreateContext(new TestTenantContext(fixture.TenantId, fixture.HrUserId)); var service = Service(db, fixture); var template = await service.CreateTemplateAsync(new() { Code = "RL-SUP", Name = "Relieving supersede", DocumentType = SeparationDocumentType.RelievingLetter }); var version = await service.AddVersionAsync(template.Value!.Id, new() { BodyTemplate = "Original {{Document.Number}}" }); await service.PublishVersionAsync(template.Value.Id, version.Value!.Versions.Single().Id, 1); var original = await service.GenerateAsync(fixture.SeparationId, new() { DocumentType = SeparationDocumentType.RelievingLetter }); Assert.True(original.Succeeded, original.Message); var stored = await db.SeparationGeneratedDocuments.SingleAsync(); var oldId = stored.Id; var oldNumber = stored.DocumentNumber; var oldHash = stored.ContentHash; var oldSnapshot = stored.SnapshotJson; var oldBytes = stored.ContentBase64;
        await using var supersedeDb = database.CreateIsolatedContext(new TestTenantContext(fixture.TenantId, fixture.HrUserId)); var supersedeService = Service(supersedeDb, fixture); var replacement = await supersedeService.SupersedeAsync(oldId, new() { Reason = "Corrected historical wording" }); Assert.True(replacement.Succeeded, replacement.Message);
        await using var verifyDb = database.CreateIsolatedContext(new TestTenantContext(fixture.TenantId, fixture.HrUserId)); var rows = await verifyDb.SeparationGeneratedDocuments.OrderBy(x => x.GeneratedAtUtc).ToListAsync(); Assert.Equal(2, rows.Count); var old = rows.Single(x => x.Id == oldId); var current = rows.Single(x => x.Id != oldId); Assert.Equal(SeparationGeneratedDocumentStatus.Superseded, old.Status); Assert.Equal(current.Id, old.SupersededByDocumentId); Assert.Equal(old.Id, current.SupersedesDocumentId); Assert.Equal(oldNumber, old.DocumentNumber); Assert.Equal(oldHash, old.ContentHash); Assert.Equal(oldSnapshot, old.SnapshotJson); Assert.Equal(oldBytes, old.ContentBase64); Assert.Equal(1, rows.Count(x => x.Status == SeparationGeneratedDocumentStatus.Issued));
    }

    private static SeparationDocumentService Service(HrmsDbContext db, NoticeFixture fixture)
    {
        var tenant = new TestTenantContext(fixture.TenantId, fixture.HrUserId);
        return new(db, tenant, TimeProvider.System, new EmployeeIdentityResolver(db, tenant));
    }

    private static async Task PrepareReadyAsync(SqliteInMemoryDatabase database, NoticeFixture fixture)
    {
        await using var seed = database.CreateContext(new TestTenantContext(fixture.TenantId, fixture.HrUserId)); seed.EmployeeEmploymentHistory.RemoveRange(await seed.EmployeeEmploymentHistory.Where(x => x.EmployeeId == fixture.EmployeeId).ToListAsync()); seed.EmployeeEmploymentHistory.Add(new() { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, EffectiveFrom = new(2020, 1, 1), DesignationName = "Historical Engineer", DepartmentName = "Historical Technology", GradeLevel = "G4", EmploymentStatus = EmployeeStatus.Active }); seed.SeparationClearances.Add(new() { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeSeparationId = fixture.SeparationId, EmployeeId = fixture.EmployeeId, TemplateId = Guid.NewGuid(), Status = SeparationClearanceStatus.Completed, CompletedAtUtc = DateTime.UtcNow }); var settlement = new FinalSettlementCase { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, SeparationDate = new(2026, 10, 2), LastWorkingDate = new(2026, 10, 2), SettlementDate = new(2026, 10, 2), Status = FinalSettlementStatus.Finalized, FinalizedAtUtc = DateTime.UtcNow }; seed.FinalSettlementCases.Add(settlement); seed.SeparationSettlementOrchestrations.Add(new() { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeSeparationId = fixture.SeparationId, EmployeeId = fixture.EmployeeId, PayrollFinalSettlementId = settlement.Id, Status = SeparationSettlementOrchestrationStatus.Completed, CompletedAtUtc = DateTime.UtcNow }); await seed.SaveChangesAsync();
    }
}
