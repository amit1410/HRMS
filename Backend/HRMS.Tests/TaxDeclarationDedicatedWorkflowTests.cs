using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class TaxDeclarationWorkflowFixture : IDisposable
{
    private TaxDeclarationWorkflowFixture(SqliteInMemoryDatabase database, HrmsDbContext context, TestTenantContext tenant, TaxDeclarationService service, Guid declarationId, Guid categoryId, Guid itemId, Guid employeeId)
    { Database = database; Context = context; Tenant = tenant; Service = service; DeclarationId = declarationId; CategoryId = categoryId; ItemId = itemId; EmployeeId = employeeId; }
    public SqliteInMemoryDatabase Database { get; }
    public HrmsDbContext Context { get; }
    public TestTenantContext Tenant { get; }
    public TaxDeclarationService Service { get; }
    public Guid DeclarationId { get; }
    public Guid CategoryId { get; }
    public Guid ItemId { get; }
    public Guid EmployeeId { get; }

    public static async Task<TaxDeclarationWorkflowFixture> CreateAsync(bool requiresProof = true)
    {
        var database = new SqliteInMemoryDatabase(); var tenantId = Guid.NewGuid(); var userId = Guid.NewGuid(); var employeeId = Guid.NewGuid(); var tenant = new TestTenantContext(tenantId, userId);
        await using (var seed = database.CreateContext(tenant))
        {
            seed.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"DW{tenantId:N}"[..10], TenantName = "Dedicated workflow tenant", Host = $"{tenantId:N}.workflow.test", ShardKey = tenantId.ToString("N") });
            seed.Users.Add(new User { Id = userId, TenantId = tenantId, Email = $"{userId:N}@workflow.test", PasswordHash = "test-hash", FirstName = "Workflow", LastName = "User", IsActive = true });
            seed.Employees.Add(new Employee { Id = employeeId, TenantId = tenantId, EmployeeCode = "WF-001", FirstName = "Workflow", LastName = "Employee", Email = $"{employeeId:N}@workflow.test", DateOfJoining = new(2025, 1, 1), Status = EmployeeStatus.Active });
            var linkId = Guid.NewGuid(); seed.AccountEmployeeLinkEvents.Add(new AccountEmployeeLinkEvent { Id = linkId, TenantId = tenantId, SubjectUserId = userId, ActorUserId = userId, Sequence = 1, Operation = "Link", NewLinkId = linkId, AfterEmployeeId = employeeId, OccurredAtUtc = DateTime.UtcNow, Reason = "test", CorrelationId = linkId.ToString("N") }); seed.AccountEmployeeCurrentLinks.Add(new AccountEmployeeCurrentLink { LinkId = linkId, TenantId = tenantId, UserId = userId, EmployeeId = employeeId }); await seed.SaveChangesAsync();
        }
        var context = database.CreateContext(tenant); var service = new TaxDeclarationService(context, tenant, new EmployeeIdentityResolver(context, tenant), TimeProvider.System);
        var cycle = await service.CreateCycleAsync(new() { Code = "FY2026", Name = "FY 2026", FinancialYear = 2026, DeclarationOpenDate = new(2026, 4, 1), DeclarationCloseDate = new(2026, 9, 30), ProofSubmissionOpenDate = new(2026, 4, 1), ProofSubmissionCloseDate = new(2026, 10, 31), EffectiveFrom = new(2026, 4, 1), Status = TaxDeclarationCycleStatus.Open });
        var category = await service.CreateCategoryAsync(new() { Code = "INV", Name = "Investment", CategoryType = TaxDeclarationCategoryType.Investment, RequiresProof = requiresProof });
        var item = await service.CreateItemAsync(new() { CategoryId = category.Value!.Id, Code = "PF", Name = "Provident Fund", RequiresProof = requiresProof, PayrollTaxInputCode = "PF_DECL" });
        var declaration = await service.CreateOwnAsync(cycle.Value!.Id);
        return new(database, context, tenant, service, declaration.Value!.Id, category.Value.Id, item.Value!.Id, employeeId);
    }

    public void Dispose() { Context.Dispose(); Database.Dispose(); }
}

public sealed class TaxDeclarationSelfServiceTests
{
    [Fact]
    public async Task Employee_can_edit_and_delete_draft_but_not_mutate_submitted_or_locked_state()
    {
        using var f = await TaxDeclarationWorkflowFixture.CreateAsync();
        var line = await f.Service.AddLineOwnAsync(new() { CategoryId = f.CategoryId, ItemId = f.ItemId, DeclaredAmount = 100000m, ReferenceNumber = "SELF" }); Assert.True(line.Succeeded, line.Message); var lineId = line.Value!.Lines.Single().Id;
        Assert.True((await f.Service.UpdateLineOwnAsync(f.DeclarationId, lineId, new() { DeclaredAmount = 90000m })).Succeeded);
        var removable = await f.Service.AddLineOwnAsync(new() { CategoryId = f.CategoryId, ItemId = f.ItemId, DeclaredAmount = 1m, ReferenceNumber = "REMOVE" }); Assert.True(removable.Succeeded); Assert.True((await f.Service.DeleteLineOwnAsync(f.DeclarationId, removable.Value!.Lines.Single(x => x.ReferenceNumber == "REMOVE").Id)).Succeeded);
        Assert.Equal(HRMS.Application.Common.ResultStatus.Conflict, (await f.Service.SubmitOwnAsync()).Status);
        var proof = await f.Service.AddProofOwnAsync(new() { LineId = lineId, FileName = "proof.pdf", ContentType = "application/pdf", StorageReference = "opaque/proof", FileSize = 1 }); Assert.True(proof.Succeeded);
        var submitted = await f.Service.SubmitOwnAsync(); Assert.True(submitted.Succeeded);
        Assert.Equal(HRMS.Application.Common.ResultStatus.Conflict, (await f.Service.UpdateLineOwnAsync(f.DeclarationId, lineId, new() { DeclaredAmount = 1m })).Status);
        Assert.Equal(HRMS.Application.Common.ResultStatus.Conflict, (await f.Service.DeleteLineOwnAsync(f.DeclarationId, lineId)).Status);
        Assert.True((await f.Service.ReviewLineAsync(f.DeclarationId, new() { LineId = lineId, Decision = EmployeeTaxDeclarationLineStatus.Approved, ApprovedAmount = 90000m, Comment = "approved" })).Succeeded);
        Assert.True((await f.Service.ApproveAsync(f.DeclarationId)).Succeeded); Assert.True((await f.Service.LockAsync(f.DeclarationId)).Succeeded);
        Assert.Equal(HRMS.Application.Common.ResultStatus.Conflict, (await f.Service.UpdateLineOwnAsync(f.DeclarationId, lineId, new() { DeclaredAmount = 2m })).Status);
        Assert.NotNull(proof.Value);
    }
}

public sealed class TaxDeclarationReviewerTests
{
    [Fact]
    public async Task Reviewer_queue_partial_approval_proof_decision_and_lock_reopen_are_audited()
    {
        using var f = await TaxDeclarationWorkflowFixture.CreateAsync(); var line = await f.Service.AddLineOwnAsync(new() { CategoryId = f.CategoryId, ItemId = f.ItemId, DeclaredAmount = 100000m }); var lineId = line.Value!.Lines.Single().Id; var proof = await f.Service.AddProofOwnAsync(new() { LineId = lineId, FileName = "proof.pdf", ContentType = "application/pdf", StorageReference = "opaque/proof", FileSize = 1 }); Assert.True((await f.Service.SubmitOwnAsync()).Succeeded);
        var queue = await f.Service.GetReviewAsync(new() { Page = 1, PageSize = 50, Status = EmployeeTaxDeclarationStatus.Submitted }); Assert.True(queue.Succeeded); Assert.Single(queue.Value!.Items);
        Assert.True((await f.Service.ReviewProofAsync(f.DeclarationId, lineId, proof.Value!.Id, new() { Decision = TaxDeclarationProofStatus.Accepted, Comment = "accepted" })).Succeeded);
        var reviewed = await f.Service.ReviewLineAsync(f.DeclarationId, new() { LineId = lineId, Decision = EmployeeTaxDeclarationLineStatus.PartiallyApproved, ApprovedAmount = 75000m, Comment = "partial" }); Assert.True(reviewed.Succeeded); Assert.True((await f.Service.ApproveAsync(f.DeclarationId)).Succeeded);
        var resolved = await f.Service.ResolveApprovedAsync(f.EmployeeId, 2026, new(2026, 9, 30)); Assert.Equal(75000m, Assert.Single(resolved.Value!).ApprovedAmount);
        Assert.True((await f.Service.LockAsync(f.DeclarationId)).Succeeded); Assert.True((await f.Service.ReopenAsync(f.DeclarationId, "correction")).Succeeded);
        var audit = await f.Service.GetAuditAsync(f.DeclarationId); Assert.Contains(audit.Value!, x => x.Action == TaxDeclarationAuditAction.ProofAccepted); Assert.Contains(audit.Value!, x => x.Action == TaxDeclarationAuditAction.LinePartiallyApproved); Assert.Contains(audit.Value!, x => x.Action == TaxDeclarationAuditAction.Reopened);
    }
}

public sealed class TaxDeclarationProofWorkflowTests
{
    [Fact]
    public async Task Rejected_proof_is_replaced_non_destructively_and_replacement_is_reviewable()
    {
        using var f = await TaxDeclarationWorkflowFixture.CreateAsync(); var line = await f.Service.AddLineOwnAsync(new() { CategoryId = f.CategoryId, ItemId = f.ItemId, DeclaredAmount = 100m }); var lineId = line.Value!.Lines.Single().Id; var oldProof = await f.Service.AddProofOwnAsync(new() { LineId = lineId, FileName = "old.pdf", ContentType = "application/pdf", StorageReference = "opaque/old", FileSize = 1 }); Assert.True((await f.Service.SubmitOwnAsync()).Succeeded);
        Assert.True((await f.Service.ReviewProofAsync(f.DeclarationId, lineId, oldProof.Value!.Id, new() { Decision = TaxDeclarationProofStatus.Rejected, Comment = "Unreadable" })).Succeeded); Assert.True((await f.Service.RequestResubmissionAsync(f.DeclarationId, "replace proof")).Succeeded);
        var replacement = await f.Service.ReplaceProofOwnAsync(f.DeclarationId, lineId, oldProof.Value.Id, new() { LineId = lineId, FileName = "new.pdf", ContentType = "application/pdf", StorageReference = "opaque/new", FileSize = 2 }); Assert.True(replacement.Succeeded); Assert.True((await f.Service.ResubmitOwnAsync(f.DeclarationId)).Succeeded); Assert.True((await f.Service.ReviewProofAsync(f.DeclarationId, lineId, replacement.Value!.Id, new() { Decision = TaxDeclarationProofStatus.Accepted, Comment = "accepted" })).Succeeded);
        var proofs = await f.Context.TaxDeclarationProofs.Where(x => x.EmployeeTaxDeclarationLineId == lineId).ToListAsync(); Assert.Equal(2, proofs.Count); Assert.Equal(TaxDeclarationProofStatus.Replaced, proofs.Single(x => x.FileName == "old.pdf").Status); Assert.Equal(TaxDeclarationProofStatus.Accepted, proofs.Single(x => x.FileName == "new.pdf").Status); Assert.Equal("Unreadable", proofs.Single(x => x.FileName == "old.pdf").ReviewerComment);
        var audit = await f.Service.GetAuditAsync(f.DeclarationId); Assert.Contains(audit.Value!, x => x.Action == TaxDeclarationAuditAction.ProofRejected); Assert.Contains(audit.Value!, x => x.Action == TaxDeclarationAuditAction.ProofReplaced);
    }
}

public sealed class TaxDeclarationResubmissionTests
{
    [Fact]
    public async Task Full_resubmission_lifecycle_preserves_history_and_uses_approved_amount()
    {
        using var f = await TaxDeclarationWorkflowFixture.CreateAsync(); var line = await f.Service.AddLineOwnAsync(new() { CategoryId = f.CategoryId, ItemId = f.ItemId, DeclaredAmount = 100000m }); var lineId = line.Value!.Lines.Single().Id; var proof = await f.Service.AddProofOwnAsync(new() { LineId = lineId, FileName = "old.pdf", ContentType = "application/pdf", StorageReference = "opaque/old", FileSize = 1 }); Assert.True((await f.Service.SubmitOwnAsync()).Succeeded);
        Assert.True((await f.Service.ReviewProofAsync(f.DeclarationId, lineId, proof.Value!.Id, new() { Decision = TaxDeclarationProofStatus.Rejected, Comment = "replace" })).Succeeded); Assert.True((await f.Service.RequestResubmissionAsync(f.DeclarationId, "correct evidence")).Succeeded); Assert.True((await f.Service.UpdateLineOwnAsync(f.DeclarationId, lineId, new() { DeclaredAmount = 100000m, Notes = "corrected" })).Succeeded);
        var replacement = await f.Service.ReplaceProofOwnAsync(f.DeclarationId, lineId, proof.Value.Id, new() { LineId = lineId, FileName = "new.pdf", ContentType = "application/pdf", StorageReference = "opaque/new", FileSize = 1 }); Assert.True((await f.Service.ResubmitOwnAsync(f.DeclarationId)).Succeeded); Assert.True((await f.Service.ReviewProofAsync(f.DeclarationId, lineId, replacement.Value!.Id, new() { Decision = TaxDeclarationProofStatus.Accepted })).Succeeded); Assert.True((await f.Service.ReviewLineAsync(f.DeclarationId, new() { LineId = lineId, Decision = EmployeeTaxDeclarationLineStatus.PartiallyApproved, ApprovedAmount = 75000m, Comment = "partial" })).Succeeded); Assert.True((await f.Service.ApproveAsync(f.DeclarationId)).Succeeded); Assert.True((await f.Service.LockAsync(f.DeclarationId)).Succeeded);
        var resolved = await f.Service.ResolveApprovedAsync(f.EmployeeId, 2026, new(2026, 9, 30)); Assert.Equal(75000m, Assert.Single(resolved.Value!).ApprovedAmount); var audit = await f.Service.GetAuditAsync(f.DeclarationId); Assert.Contains(audit.Value!, x => x.Action == TaxDeclarationAuditAction.Resubmitted); Assert.Contains(audit.Value!, x => x.Action == TaxDeclarationAuditAction.ProofReplaced); Assert.Contains(audit.Value!, x => x.Action == TaxDeclarationAuditAction.Locked);
    }
}
