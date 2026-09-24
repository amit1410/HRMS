using HRMS.Application.Abstractions;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Entities.Separation;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class SeparationDocumentFailureInjectionTests
{
    [Fact]
    public async Task Number_allocation_or_generation_failure_rolls_back_safely()
    {
        using var database = new SqliteInMemoryDatabase();
        var fixture = await PrepareReadyAsync(database);
        await using var db = database.CreateContext(new TestTenantContext(fixture.TenantId, fixture.HrUserId));
        var setup = await CreateTemplateAsync(db, fixture, "FAIL-GEN", false);
        var failed = await CreateService(db, fixture, new InjectedFailure(Generation: true)).GenerateAsync(fixture.SeparationId, new() { DocumentType = SeparationDocumentType.RelievingLetter, TemplateVersionId = setup.VersionId });
        Assert.False(failed.Succeeded);
        Assert.Empty(await db.SeparationGeneratedDocuments.AsNoTracking().ToListAsync());
        await using var retryDb = database.CreateContext(new TestTenantContext(fixture.TenantId, fixture.HrUserId));
        var retried = await CreateService(retryDb, fixture).GenerateAsync(fixture.SeparationId, new() { DocumentType = SeparationDocumentType.RelievingLetter, TemplateVersionId = setup.VersionId });
        Assert.True(retried.Succeeded, retried.Message);
        Assert.Single(await retryDb.SeparationGeneratedDocuments.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Storage_persistence_failure_is_recoverable()
    {
        using var database = new SqliteInMemoryDatabase();
        var fixture = await PrepareReadyAsync(database);
        await using var db = database.CreateContext(new TestTenantContext(fixture.TenantId, fixture.HrUserId));
        var setup = await CreateTemplateAsync(db, fixture, "FAIL-STORE", false);
        var failed = await CreateService(db, fixture, new InjectedFailure(Storage: true)).GenerateAsync(fixture.SeparationId, new() { DocumentType = SeparationDocumentType.RelievingLetter, TemplateVersionId = setup.VersionId });
        Assert.False(failed.Succeeded);
        Assert.Empty(await db.SeparationGeneratedDocuments.AsNoTracking().ToListAsync());
        await using var retryDb = database.CreateContext(new TestTenantContext(fixture.TenantId, fixture.HrUserId));
        var retried = await CreateService(retryDb, fixture).GenerateAsync(fixture.SeparationId, new() { DocumentType = SeparationDocumentType.RelievingLetter, TemplateVersionId = setup.VersionId });
        Assert.True(retried.Succeeded, retried.Message);
        Assert.Single(await retryDb.SeparationGeneratedDocuments.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Approval_or_issue_failure_is_replay_safe()
    {
        using var database = new SqliteInMemoryDatabase();
        var fixture = await PrepareReadyAsync(database);
        await using var makerDb = database.CreateContext(new TestTenantContext(fixture.TenantId, fixture.HrUserId));
        var setup = await CreateTemplateAsync(makerDb, fixture, "FAIL-APPROVE", true);
        var generated = await CreateService(makerDb, fixture).GenerateAsync(fixture.SeparationId, new() { DocumentType = SeparationDocumentType.RelievingLetter, TemplateVersionId = setup.VersionId });
        Assert.True(generated.Succeeded, generated.Message);
        await using var checkerDb = database.CreateContext(new TestTenantContext(fixture.TenantId, Guid.NewGuid()));
        var checkerUserId = Guid.NewGuid();
        var checker = CreateService(checkerDb, fixture, new InjectedFailure(Approval: true), checkerUserId);
        var failed = await checker.ApproveAsync(generated.Value!.Id, new());
        Assert.False(failed.Succeeded);
        var retry = await CreateService(checkerDb, fixture, null, checkerUserId).ApproveAsync(generated.Value.Id, new());
        Assert.True(retry.Succeeded, retry.Message);
        var repeated = await CreateService(checkerDb, fixture, null, checkerUserId).ApproveAsync(generated.Value.Id, new());
        Assert.True(repeated.Succeeded, repeated.Message);
        Assert.Equal(1, await checkerDb.SeparationDocumentEvents.CountAsync(x => x.GeneratedDocumentId == generated.Value.Id && x.EventType == SeparationDocumentEventType.DocumentApproved));
    }

    private static SeparationDocumentService CreateService(HrmsDbContext db, NoticeFixture fixture, ISeparationDocumentFailureInjector? injector = null, Guid? userId = null)
    {
        var tenant = new TestTenantContext(fixture.TenantId, userId ?? fixture.HrUserId);
        return new(db, tenant, TimeProvider.System, new EmployeeIdentityResolver(db, tenant), injector);
    }

    private static async Task<(NoticeFixture Fixture, Guid VersionId)> CreateTemplateAsync(HrmsDbContext db, NoticeFixture fixture, string code, bool approval)
    {
        var service = CreateService(db, fixture);
        var template = await service.CreateTemplateAsync(new() { Code = code, Name = code, DocumentType = SeparationDocumentType.RelievingLetter, RequiresApproval = approval });
        Assert.True(template.Succeeded, template.Message);
        var version = await service.AddVersionAsync(template.Value!.Id, new() { BodyTemplate = "{{Employee.FullName}}|{{Document.Number}}" });
        Assert.True(version.Succeeded, version.Message);
        var published = await service.PublishVersionAsync(template.Value.Id, version.Value!.Versions.Single().Id, 1);
        Assert.True(published.Succeeded, published.Message);
        return (fixture, version.Value.Versions.Single().Id);
    }

    private static async Task<NoticeFixture> PrepareReadyAsync(SqliteInMemoryDatabase database)
    {
        var fixture = await NoticeTestData.CreateApprovedAsync(database, new(2026, 10, 2));
        await using var seed = database.CreateContext(new TestTenantContext(fixture.TenantId, fixture.HrUserId));
        seed.EmployeeEmploymentHistory.RemoveRange(await seed.EmployeeEmploymentHistory.Where(x => x.EmployeeId == fixture.EmployeeId).ToListAsync());
        seed.EmployeeEmploymentHistory.Add(new() { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, EffectiveFrom = new(2020, 1, 1), DesignationName = "Historical Engineer", DepartmentName = "Historical Technology", GradeLevel = "G4", EmploymentStatus = EmployeeStatus.Active });
        seed.SeparationClearances.Add(new() { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeSeparationId = fixture.SeparationId, EmployeeId = fixture.EmployeeId, TemplateId = Guid.NewGuid(), Status = SeparationClearanceStatus.Completed, CompletedAtUtc = DateTime.UtcNow });
        var settlement = new FinalSettlementCase { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, SeparationDate = new(2026, 10, 2), LastWorkingDate = new(2026, 10, 2), SettlementDate = new(2026, 10, 2), Status = FinalSettlementStatus.Finalized, FinalizedAtUtc = DateTime.UtcNow };
        seed.FinalSettlementCases.Add(settlement);
        seed.SeparationSettlementOrchestrations.Add(new() { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeSeparationId = fixture.SeparationId, EmployeeId = fixture.EmployeeId, PayrollFinalSettlementId = settlement.Id, Status = SeparationSettlementOrchestrationStatus.Completed, CompletedAtUtc = DateTime.UtcNow });
        await seed.SaveChangesAsync();
        return fixture;
    }

    private sealed class InjectedFailure(bool Generation = false, bool Storage = false, bool Approval = false) : ISeparationDocumentFailureInjector
    {
        public void BeforeGenerationCommit() { if (Generation) throw new InvalidOperationException("deterministic generation failure"); }
        public void BeforeStorageCommit() { if (Storage) throw new InvalidOperationException("deterministic storage failure"); }
        public void BeforeApprovalCommit() { if (Approval) throw new InvalidOperationException("deterministic approval failure"); }
    }
}
