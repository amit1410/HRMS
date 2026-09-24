using HRMS.API.Controllers;
using HRMS.API.Security;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Separation;
using HRMS.Application.Services;
using HRMS.Domain.Authorization;
using HRMS.Domain.Entities;
using HRMS.Domain.Entities.Separation;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class SeparationDocumentSecurityTests
{
    [Fact]
    public async Task Employee_cannot_fetch_other_employee_document()
    {
        using var database = new SqliteInMemoryDatabase();
        var owner = await CreateIssuedAsync(database);
        await using var db = database.CreateContext(new TestTenantContext(owner.Fixture.TenantId, Guid.NewGuid()));
        var result = await Service(db, owner.Fixture, Guid.NewGuid()).GetForSeparationAsync(owner.Fixture.SeparationId, true);
        Assert.False(result.Succeeded);
        Assert.Contains(result.Status, new[] { ResultStatus.Forbidden, ResultStatus.NotFound, ResultStatus.Unauthorized });
    }

    [Fact]
    public async Task Cross_tenant_document_id_denied()
    {
        using var database = new SqliteInMemoryDatabase();
        var foreign = await CreateIssuedAsync(database);
        var localTenantId = Guid.NewGuid();
        await using var db = database.CreateContext(new TestTenantContext(localTenantId, Guid.NewGuid()));
        var localTenant = new TestTenantContext(localTenantId, Guid.NewGuid());
        var result = await new SeparationDocumentService(db, localTenant, TimeProvider.System, new EmployeeIdentityResolver(db, localTenant)).DownloadAsync(foreign.DocumentId);
        Assert.False(result.Succeeded);
        Assert.Contains(result.Status, new[] { ResultStatus.Forbidden, ResultStatus.NotFound });
    }

    [Fact]
    public async Task Employee_cannot_view_internal_draft()
    {
        using var database = new SqliteInMemoryDatabase();
        var fixture = await PrepareReadyAsync(database);
        await using var db = database.CreateContext(new TestTenantContext(fixture.TenantId, fixture.HrUserId));
        var setup = await CreateTemplateAsync(db, fixture, true, "SEC-DRAFT");
        var generated = await Service(db, fixture).GenerateAsync(fixture.SeparationId, new() { DocumentType = SeparationDocumentType.RelievingLetter, TemplateVersionId = setup });
        Assert.True(generated.Succeeded, generated.Message);
        await using var employeeDb = database.CreateContext(new TestTenantContext(fixture.TenantId, fixture.EmployeeUserId));
        var result = await Service(employeeDb, fixture, fixture.EmployeeUserId).GetForSeparationAsync(fixture.SeparationId, true);
        Assert.Empty(result.Value ?? []);
    }

    [Fact]
    public void Employee_cannot_view_internal_approval_metadata()
    {
        var names = typeof(SeparationGeneratedDocumentDto).GetProperties().Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("SnapshotJson", names);
        Assert.DoesNotContain("StorageReference", names);
        Assert.DoesNotContain("GeneratedByUserId", names);
        Assert.DoesNotContain("ApprovedByUserId", names);
    }

    [Fact]
    public void Unauthorized_hr_cannot_generate()
    {
        var method = typeof(SeparationDocumentController).GetMethod(nameof(SeparationDocumentController.Generate));
        Assert.Contains(method!.GetCustomAttributes(typeof(HasPermissionAttribute), true).Cast<HasPermissionAttribute>(), x => x.Permission == Permissions.Separation.Manage);
    }

    [Fact]
    public void Unauthorized_user_cannot_approve()
    {
        var method = typeof(SeparationDocumentController).GetMethod(nameof(SeparationDocumentController.Approve));
        Assert.Contains(method!.GetCustomAttributes(typeof(HasPermissionAttribute), true).Cast<HasPermissionAttribute>(), x => x.Permission == Permissions.Separation.Manage);
    }

    [Fact]
    public async Task Unknown_merge_field_rejected()
    {
        using var database = new SqliteInMemoryDatabase();
        var fixture = await NoticeTestData.CreateApprovedAsync(database, new(2026, 10, 2));
        await using var db = database.CreateContext(new TestTenantContext(fixture.TenantId, fixture.HrUserId));
        var template = await Service(db, fixture).CreateTemplateAsync(new() { Code = "SEC-TOKEN", Name = "Security", DocumentType = SeparationDocumentType.RelievingLetter });
        var result = await Service(db, fixture).AddVersionAsync(template.Value!.Id, new() { BodyTemplate = "{{Employee.Secret}}" });
        Assert.Equal(ResultStatus.ValidationFailed, result.Status);
    }

    [Fact]
    public async Task Unsafe_template_script_does_not_execute()
    {
        using var database = new SqliteInMemoryDatabase();
        var fixture = await NoticeTestData.CreateApprovedAsync(database, new(2026, 10, 2));
        await using var db = database.CreateContext(new TestTenantContext(fixture.TenantId, fixture.HrUserId));
        var template = await Service(db, fixture).CreateTemplateAsync(new() { Code = "SEC-SCRIPT", Name = "Security", DocumentType = SeparationDocumentType.RelievingLetter });
        var result = await Service(db, fixture).AddVersionAsync(template.Value!.Id, new() { BodyTemplate = "<script>alert(1)</script>" });
        Assert.Equal(ResultStatus.ValidationFailed, result.Status);
    }

    [Fact]
    public async Task Renderer_cannot_fetch_arbitrary_remote_url()
    {
        using var database = new SqliteInMemoryDatabase();
        var fixture = await NoticeTestData.CreateApprovedAsync(database, new(2026, 10, 2));
        await using var db = database.CreateContext(new TestTenantContext(fixture.TenantId, fixture.HrUserId));
        var template = await Service(db, fixture).CreateTemplateAsync(new() { Code = "SEC-URL", Name = "Security", DocumentType = SeparationDocumentType.RelievingLetter });
        var result = await Service(db, fixture).AddVersionAsync(template.Value!.Id, new() { BodyTemplate = "http://localhost:80/internal" });
        Assert.Equal(ResultStatus.ValidationFailed, result.Status);
    }

    [Fact]
    public async Task Download_path_cannot_traverse()
    {
        using var database = new SqliteInMemoryDatabase();
        var owner = await CreateIssuedAsync(database);
        await using var db = database.CreateContext(new TestTenantContext(owner.Fixture.TenantId, owner.Fixture.HrUserId));
        var result = await Service(db, owner.Fixture).DownloadAsync(Guid.NewGuid());
        Assert.Equal(ResultStatus.NotFound, result.Status);
    }

    private static SeparationDocumentService Service(HrmsDbContext db, NoticeFixture fixture, Guid? userId = null)
    {
        var tenant = new TestTenantContext(fixture.TenantId, userId ?? fixture.HrUserId);
        return new(db, tenant, TimeProvider.System, new EmployeeIdentityResolver(db, tenant));
    }

    private static async Task<Guid> CreateTemplateAsync(HrmsDbContext db, NoticeFixture fixture, bool approval, string code)
    {
        var service = Service(db, fixture);
        var template = await service.CreateTemplateAsync(new() { Code = code, Name = code, DocumentType = SeparationDocumentType.RelievingLetter, RequiresApproval = approval });
        var version = await service.AddVersionAsync(template.Value!.Id, new() { BodyTemplate = "{{Employee.FullName}}|{{Document.Number}}" });
        await service.PublishVersionAsync(template.Value.Id, version.Value!.Versions.Single().Id, 1);
        return version.Value.Versions.Single().Id;
    }

    private static async Task<(NoticeFixture Fixture, Guid DocumentId)> CreateIssuedAsync(SqliteInMemoryDatabase database)
    {
        var fixture = await PrepareReadyAsync(database);
        await using var db = database.CreateContext(new TestTenantContext(fixture.TenantId, fixture.HrUserId));
        var versionId = await CreateTemplateAsync(db, fixture, false, $"SEC-{Guid.NewGuid():N}");
        var generated = await Service(db, fixture).GenerateAsync(fixture.SeparationId, new() { DocumentType = SeparationDocumentType.RelievingLetter, TemplateVersionId = versionId });
        Assert.True(generated.Succeeded, generated.Message);
        return (fixture, generated.Value!.Id);
    }

    private static async Task<NoticeFixture> PrepareReadyAsync(SqliteInMemoryDatabase database)
    {
        var fixture = await NoticeTestData.CreateApprovedAsync(database, new(2026, 10, 2));
        await using var seed = database.CreateContext(new TestTenantContext(fixture.TenantId, fixture.HrUserId));
        seed.EmployeeEmploymentHistory.RemoveRange(await seed.EmployeeEmploymentHistory.Where(x => x.EmployeeId == fixture.EmployeeId).ToListAsync());
        seed.EmployeeEmploymentHistory.Add(new() { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, EffectiveFrom = new(2020, 1, 1), DesignationName = "Security Engineer", DepartmentName = "Security", GradeLevel = "G4", EmploymentStatus = EmployeeStatus.Active });
        seed.SeparationClearances.Add(new() { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeSeparationId = fixture.SeparationId, EmployeeId = fixture.EmployeeId, TemplateId = Guid.NewGuid(), Status = SeparationClearanceStatus.Completed, CompletedAtUtc = DateTime.UtcNow });
        var settlement = new FinalSettlementCase { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, SeparationDate = new(2026, 10, 2), LastWorkingDate = new(2026, 10, 2), SettlementDate = new(2026, 10, 2), Status = FinalSettlementStatus.Finalized, FinalizedAtUtc = DateTime.UtcNow };
        seed.FinalSettlementCases.Add(settlement);
        seed.SeparationSettlementOrchestrations.Add(new() { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeSeparationId = fixture.SeparationId, EmployeeId = fixture.EmployeeId, PayrollFinalSettlementId = settlement.Id, Status = SeparationSettlementOrchestrationStatus.Completed, CompletedAtUtc = DateTime.UtcNow });
        await seed.SaveChangesAsync();
        return fixture;
    }
}
