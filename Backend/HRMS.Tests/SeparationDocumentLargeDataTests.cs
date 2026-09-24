using HRMS.Application.Services;
using HRMS.Application.DTOs.Separation;
using HRMS.Domain.Entities;
using HRMS.Domain.Entities.Separation;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class SeparationDocumentLargeDataTests
{
    [Fact]
    public async Task Renders_bounded_representative_sample_with_real_service_artifacts()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid();
        var reasonId = Guid.NewGuid();
        var relievingTemplateId = Guid.NewGuid();
        var experienceTemplateId = Guid.NewGuid();
        var relievingVersionId = Guid.NewGuid();
        var experienceVersionId = Guid.NewGuid();
        var employees = Enumerable.Range(1, 10).Select(i => new Employee { Id = Guid.NewGuid(), TenantId = tenantId, FirstName = "Render", LastName = $"Sample{i}", Email = $"render{i}@example.test", DateOfJoining = new(2020, 1, 1), EmployeeCode = $"RENDER-{i:000}" }).ToList();
        var separations = employees.Select((employee, i) => new EmployeeSeparation { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employee.Id, ReasonId = reasonId, SeparationType = SeparationType.EmployeeInitiated, SeparationNumber = $"RENDER-SEP-{i:000}", RequestDate = new(2026, 1, 1), ProposedLastWorkingDate = new(2026, 10, 2), ApprovedLastWorkingDate = new(2026, 10, 2), Status = EmployeeSeparationStatus.Approved }).ToList();

        await using (var seed = database.CreateContext(new TestTenantContext(tenantId, Guid.NewGuid())))
        {
            seed.Tenants.Add(new Tenant { Id = tenantId, TenantCode = "RENDER", TenantName = "Render Tenant", Address = "Render Address" });
            seed.SeparationReasons.Add(new HRMS.Domain.Entities.Separation.SeparationReason { Id = reasonId, TenantId = tenantId, Code = "R", Name = "Resignation", EffectiveFrom = new(2020, 1, 1) });
            seed.Employees.AddRange(employees);
            seed.EmployeeSeparations.AddRange(separations);
            seed.SeparationDocumentTemplates.AddRange(
                new SeparationDocumentTemplate { Id = relievingTemplateId, TenantId = tenantId, Code = "RL", Name = "Relieving", DocumentType = SeparationDocumentType.RelievingLetter, EffectiveFrom = new(2020, 1, 1) },
                new SeparationDocumentTemplate { Id = experienceTemplateId, TenantId = tenantId, Code = "EL", Name = "Experience", DocumentType = SeparationDocumentType.ExperienceLetter, EffectiveFrom = new(2020, 1, 1) });
            seed.SeparationDocumentTemplateVersions.AddRange(
                new SeparationDocumentTemplateVersion { Id = relievingVersionId, TenantId = tenantId, TemplateId = relievingTemplateId, VersionNumber = 1, Status = SeparationDocumentTemplateVersionStatus.Published, EffectiveFrom = new(2020, 1, 1), BodyTemplate = "{{Employee.FullName}}|{{Employee.EmployeeCode}}|{{Separation.ApprovedLastWorkingDate}}|{{Document.Number}}" },
                new SeparationDocumentTemplateVersion { Id = experienceVersionId, TenantId = tenantId, TemplateId = experienceTemplateId, VersionNumber = 1, Status = SeparationDocumentTemplateVersionStatus.Published, EffectiveFrom = new(2020, 1, 1), BodyTemplate = "{{Employee.FullName}}|{{Employee.EmployeeCode}}|{{Separation.ApprovedLastWorkingDate}}|{{Document.Number}}" });
            foreach (var separation in separations)
            {
                seed.EmployeeEmploymentHistory.Add(new EmployeeEmploymentHistory { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = separation.EmployeeId, EffectiveFrom = new(2020, 1, 1), DesignationName = "Historical Engineer", DepartmentName = "Historical Technology", GradeLevel = "G4", EmploymentStatus = EmployeeStatus.Active });
                seed.SeparationClearances.Add(new SeparationClearance { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeSeparationId = separation.Id, EmployeeId = separation.EmployeeId, TemplateId = Guid.NewGuid(), Status = SeparationClearanceStatus.Completed, CompletedAtUtc = DateTime.UtcNow });
                var settlement = new FinalSettlementCase { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = separation.EmployeeId, SeparationDate = new(2026, 10, 2), LastWorkingDate = new(2026, 10, 2), SettlementDate = new(2026, 10, 2), Status = FinalSettlementStatus.Finalized, FinalizedAtUtc = DateTime.UtcNow };
                seed.FinalSettlementCases.Add(settlement);
                seed.SeparationSettlementOrchestrations.Add(new SeparationSettlementOrchestration { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeSeparationId = separation.Id, EmployeeId = separation.EmployeeId, PayrollFinalSettlementId = settlement.Id, Status = SeparationSettlementOrchestrationStatus.Completed, CompletedAtUtc = DateTime.UtcNow });
            }
            await seed.SaveChangesAsync();
        }

        await using var db = database.CreateContext(new TestTenantContext(tenantId, Guid.NewGuid()));
        var tenant = new TestTenantContext(tenantId, Guid.NewGuid());
        var service = new SeparationDocumentService(db, tenant, TimeProvider.System, new EmployeeIdentityResolver(db, tenant));
        var generated = new List<SeparationGeneratedDocumentDto>();
        foreach (var separation in separations)
        {
            var relieving = await service.GenerateAsync(separation.Id, new() { DocumentType = SeparationDocumentType.RelievingLetter, TemplateVersionId = relievingVersionId });
            var experience = await service.GenerateAsync(separation.Id, new() { DocumentType = SeparationDocumentType.ExperienceLetter, TemplateVersionId = experienceVersionId });
            Assert.True(relieving.Succeeded, relieving.Message);
            Assert.True(experience.Succeeded, experience.Message);
            generated.Add(relieving.Value!);
            generated.Add(experience.Value!);
        }

        Assert.Equal(20, generated.Count);
        foreach (var document in generated)
        {
            Assert.NotEqual(Guid.Empty, document.EmployeeId);
            Assert.NotEqual(Guid.Empty, document.SeparationId);
            Assert.NotEqual(Guid.Empty, document.TemplateVersionId);
            Assert.False(string.IsNullOrWhiteSpace(document.ContentHash));
            var stored = await db.SeparationGeneratedDocuments.AsNoTracking().Include(x => x.Employee).SingleAsync(x => x.Id == document.Id);
            Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(Convert.FromBase64String(stored.ContentBase64)));
            Assert.Contains($"Render {stored.Employee!.LastName}", stored.SnapshotJson, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Dashboard_and_employee_lookup_scale_across_one_thousand_separations()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid();
        var reasonId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var experienceTemplateId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var experienceVersionId = Guid.NewGuid();
        var employees = Enumerable.Range(1, 1000).Select(i => new Employee
        {
            Id = Guid.NewGuid(), TenantId = tenantId, FirstName = "Scale", LastName = $"User{i}",
            Email = $"scale{i}@example.test", DateOfJoining = new(2020, 1, 1), EmployeeCode = $"SCALE-{i:0000}"
        }).ToList();
        var separations = employees.Select((employee, i) => new EmployeeSeparation
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employee.Id, ReasonId = reasonId,
            SeparationType = SeparationType.EmployeeInitiated, SeparationNumber = $"SEP-{i + 1:0000}",
            RequestDate = new(2026, 1, 1), ProposedLastWorkingDate = new(2026, 10, 2),
            ApprovedLastWorkingDate = new(2026, 10, 2), Status = EmployeeSeparationStatus.Approved
        }).ToList();

        await using (var seed = database.CreateContext(new TestTenantContext(tenantId)))
        {
            seed.Tenants.Add(new Tenant { Id = tenantId, TenantCode = "SCALE", TenantName = "Scale Tenant", Address = "Scale Address" });
            seed.SeparationReasons.Add(new HRMS.Domain.Entities.Separation.SeparationReason { Id = reasonId, TenantId = tenantId, Code = "R", Name = "Resignation", EffectiveFrom = new(2020, 1, 1) });
            seed.Employees.AddRange(employees);
            seed.EmployeeSeparations.AddRange(separations);
            seed.SeparationDocumentTemplates.AddRange(
                new SeparationDocumentTemplate { Id = templateId, TenantId = tenantId, Code = "RL", Name = "Relieving", DocumentType = SeparationDocumentType.RelievingLetter, EffectiveFrom = new(2020, 1, 1) },
                new SeparationDocumentTemplate { Id = experienceTemplateId, TenantId = tenantId, Code = "EL", Name = "Experience", DocumentType = SeparationDocumentType.ExperienceLetter, EffectiveFrom = new(2020, 1, 1) });
            seed.SeparationDocumentTemplateVersions.AddRange(
                new SeparationDocumentTemplateVersion { Id = versionId, TenantId = tenantId, TemplateId = templateId, VersionNumber = 1, Status = SeparationDocumentTemplateVersionStatus.Published, EffectiveFrom = new(2020, 1, 1), BodyTemplate = "Scale" },
                new SeparationDocumentTemplateVersion { Id = experienceVersionId, TenantId = tenantId, TemplateId = experienceTemplateId, VersionNumber = 1, Status = SeparationDocumentTemplateVersionStatus.Published, EffectiveFrom = new(2020, 1, 1), BodyTemplate = "Scale" });
            await seed.SaveChangesAsync();
        }

        await using (var seed = database.CreateContext(new TestTenantContext(tenantId)))
        {
            seed.SeparationGeneratedDocuments.AddRange(separations.SelectMany((separation, index) => new[]
            {
                new SeparationGeneratedDocument { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeSeparationId = separation.Id, EmployeeId = separation.EmployeeId, DocumentType = SeparationDocumentType.RelievingLetter, TemplateId = templateId, TemplateVersionId = versionId, DocumentNumber = $"RL/2026/{index + 1:000000}", Status = SeparationGeneratedDocumentStatus.Issued, IssueDate = new(2026, 10, 2), GeneratedAtUtc = DateTime.UtcNow.AddSeconds(-index), ContentHash = $"rl-hash-{index}", FileName = $"rl-{index}.pdf", SnapshotJson = "{}", ContentBase64 = "" },
                new SeparationGeneratedDocument { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeSeparationId = separation.Id, EmployeeId = separation.EmployeeId, DocumentType = SeparationDocumentType.ExperienceLetter, TemplateId = experienceTemplateId, TemplateVersionId = experienceVersionId, DocumentNumber = $"EL/2026/{index + 1:000000}", Status = SeparationGeneratedDocumentStatus.Superseded, IssueDate = new(2026, 10, 2), GeneratedAtUtc = DateTime.UtcNow.AddSeconds(-index), ContentHash = $"el-hash-{index}", FileName = $"el-{index}.pdf", SnapshotJson = "{}", ContentBase64 = "" }
            }));
            await seed.SaveChangesAsync();
        }
        await using var db = database.CreateContext(new TestTenantContext(tenantId)); var service = new SeparationDocumentService(db, new TestTenantContext(tenantId), TimeProvider.System, new EmployeeIdentityResolver(db, new TestTenantContext(tenantId))); var page = await service.DashboardAsync(new() { Page = 2, PageSize = 50, DocumentType = SeparationDocumentType.RelievingLetter, Status = SeparationGeneratedDocumentStatus.Issued, IssueFrom = new(2026, 1, 1), IssueTo = new(2026, 12, 31) }); Assert.True(page.Succeeded, page.Message); Assert.Equal(1000, page.Value!.TotalCount); Assert.Equal(50, page.Value.Items.Count); var lookup = await service.GetForSeparationAsync(separations[499].Id); Assert.True(lookup.Succeeded, lookup.Message); Assert.Equal(2, lookup.Value!.Count);
    }
    [Fact]
    public async Task Dashboard_pages_two_thousand_document_metadata_rows()
    {
        using var database = new SqliteInMemoryDatabase(); var tenantId = Guid.NewGuid(); var employeeId = Guid.NewGuid(); var separationId = Guid.NewGuid(); var templateId = Guid.NewGuid(); var versionId = Guid.NewGuid(); var reasonId = Guid.NewGuid();
        await using (var seed = database.CreateContext(new TestTenantContext(tenantId))) { seed.Tenants.Add(new Tenant { Id = tenantId, TenantCode = "DOC", TenantName = "Document Tenant" }); seed.Employees.Add(new Employee { Id = employeeId, TenantId = tenantId, FirstName = "Scale", LastName = "User", Email = "scale@example.test", DateOfJoining = new(2020, 1, 1), EmployeeCode = "SCALE-001" }); seed.SeparationReasons.Add(new HRMS.Domain.Entities.Separation.SeparationReason { Id = reasonId, TenantId = tenantId, Code = "R", Name = "Resignation", EffectiveFrom = new(2020, 1, 1) }); seed.EmployeeSeparations.Add(new EmployeeSeparation { Id = separationId, TenantId = tenantId, EmployeeId = employeeId, ReasonId = reasonId, SeparationType = SeparationType.EmployeeInitiated, SeparationNumber = "SEP-1", RequestDate = new(2026, 1, 1), ProposedLastWorkingDate = new(2026, 10, 2), ApprovedLastWorkingDate = new(2026, 10, 2), Status = EmployeeSeparationStatus.Approved }); seed.SeparationDocumentTemplates.Add(new SeparationDocumentTemplate { Id = templateId, TenantId = tenantId, Code = "RL", Name = "Relieving", DocumentType = SeparationDocumentType.RelievingLetter, EffectiveFrom = new(2020, 1, 1) }); seed.SeparationDocumentTemplateVersions.Add(new SeparationDocumentTemplateVersion { Id = versionId, TenantId = tenantId, TemplateId = templateId, VersionNumber = 1, Status = SeparationDocumentTemplateVersionStatus.Published, EffectiveFrom = new(2020, 1, 1), BodyTemplate = "Scale" }); await seed.SaveChangesAsync(); }
        await using (var seed = database.CreateContext(new TestTenantContext(tenantId))) { seed.SeparationGeneratedDocuments.AddRange(Enumerable.Range(1, 2000).Select(i => new SeparationGeneratedDocument { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeSeparationId = separationId, EmployeeId = employeeId, DocumentType = i % 2 == 0 ? SeparationDocumentType.RelievingLetter : SeparationDocumentType.ExperienceLetter, CurrentDocumentKey = $"fixture-{i:N}", TemplateId = templateId, TemplateVersionId = versionId, DocumentNumber = $"DOC/2026/{i:000000}", Status = SeparationGeneratedDocumentStatus.Issued, IssueDate = new(2026, 10, 2), GeneratedAtUtc = DateTime.UtcNow.AddSeconds(-i), ContentHash = "hash", FileName = $"doc-{i}.pdf", SnapshotJson = "{}", ContentBase64 = "" })); await seed.SaveChangesAsync(); }
        await using var db = database.CreateContext(new TestTenantContext(tenantId)); var service = new SeparationDocumentService(db, new TestTenantContext(tenantId), TimeProvider.System, new EmployeeIdentityResolver(db, new TestTenantContext(tenantId))); var page = await service.DashboardAsync(new() { Page = 2, PageSize = 25 }); Assert.True(page.Succeeded, page.Message); Assert.Equal(2000, page.Value!.TotalCount); Assert.Equal(25, page.Value.Items.Count);
    }
}
