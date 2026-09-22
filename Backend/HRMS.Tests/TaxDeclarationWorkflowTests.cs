using HRMS.Application.DTOs.Payroll;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;

namespace HRMS.Tests;

public sealed class TaxDeclarationWorkflowTests
{
    [Fact]
    public async Task Employee_declaration_requires_proof_and_resolves_only_approved_amount()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var tenant = new TestTenantContext(tenantId, userId);

        await using (var seed = database.CreateContext(tenant))
        {
            seed.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"TD{tenantId:N}"[..10], TenantName = "Tax declaration tenant", Host = $"{tenantId:N}.tax.test", ShardKey = tenantId.ToString("N") });
            await seed.SaveChangesAsync();
            seed.Users.Add(new User { Id = userId, TenantId = tenantId, Email = $"{userId:N}@tax.test", PasswordHash = "test-hash", FirstName = "Tax", LastName = "User", IsActive = true });
            seed.Employees.Add(new Employee { Id = employeeId, TenantId = tenantId, EmployeeCode = "EMP-TAX-001", FirstName = "Tax", LastName = "Employee", Email = $"{employeeId:N}@tax.test", DateOfJoining = new DateOnly(2025, 1, 1), Status = EmployeeStatus.Active });
            await seed.SaveChangesAsync();
            var linkId = Guid.NewGuid();
            seed.AccountEmployeeLinkEvents.Add(new AccountEmployeeLinkEvent { Id = linkId, TenantId = tenantId, SubjectUserId = userId, ActorUserId = userId, Sequence = 1, Operation = "Link", NewLinkId = linkId, AfterEmployeeId = employeeId, OccurredAtUtc = DateTime.UtcNow, Reason = "test", CorrelationId = linkId.ToString("N") });
            seed.AccountEmployeeCurrentLinks.Add(new AccountEmployeeCurrentLink { LinkId = linkId, TenantId = tenantId, UserId = userId, EmployeeId = employeeId });
            await seed.SaveChangesAsync();
        }

        await using var db = database.CreateContext(tenant);
        var service = new TaxDeclarationService(db, tenant, new EmployeeIdentityResolver(db, tenant), TimeProvider.System);
        var cycle = await service.CreateCycleAsync(new TaxDeclarationCycleRequest
        {
            Code = "FY2026",
            Name = "FY 2026 declarations",
            FinancialYear = 2026,
            DeclarationOpenDate = new DateOnly(2026, 4, 1),
            DeclarationCloseDate = new DateOnly(2026, 9, 30),
            ProofSubmissionOpenDate = new DateOnly(2026, 4, 1),
            ProofSubmissionCloseDate = new DateOnly(2026, 10, 31),
            EffectiveFrom = new DateOnly(2026, 4, 1),
            Status = TaxDeclarationCycleStatus.Open
        });
        Assert.True(cycle.Succeeded, cycle.Message);

        var category = await service.CreateCategoryAsync(new TaxDeclarationCategoryRequest { Code = "INV", Name = "Investments", CategoryType = TaxDeclarationCategoryType.Investment, RequiresProof = true });
        Assert.True(category.Succeeded, category.Message);
        var item = await service.CreateItemAsync(new TaxDeclarationItemRequest { CategoryId = category.Value!.Id, Code = "PF", Name = "Provident Fund", RequiresProof = true, PayrollTaxInputCode = "PF_DECLARED" });
        Assert.True(item.Succeeded, item.Message);

        var declaration = await service.CreateOwnAsync(cycle.Value!.Id);
        Assert.True(declaration.Succeeded, declaration.Message);
        var line = await service.AddLineOwnAsync(new TaxDeclarationLineRequest { CategoryId = category.Value.Id, ItemId = item.Value!.Id, DeclaredAmount = 100_000m, ReferenceNumber = "PF-001" });
        Assert.True(line.Succeeded, line.Message);

        var lineId = line.Value!.Lines.Single().Id;
        var updated = await service.UpdateLineOwnAsync(declaration.Value!.Id, lineId, new TaxDeclarationLineUpdateRequest { DeclaredAmount = 100_000m, ReferenceNumber = "PF-001-UPDATED", Notes = "Updated before submission" });
        Assert.True(updated.Succeeded, updated.Message);
        var removable = await service.AddLineOwnAsync(new TaxDeclarationLineRequest { CategoryId = category.Value.Id, ItemId = item.Value.Id, DeclaredAmount = 1_000m, ReferenceNumber = "REMOVE" });
        Assert.True(removable.Succeeded, removable.Message);
        var removed = await service.DeleteLineOwnAsync(declaration.Value.Id, removable.Value!.Lines.Single(x => x.ReferenceNumber == "REMOVE").Id);
        Assert.True(removed.Succeeded, removed.Message);

        var blocked = await service.SubmitOwnAsync();
        Assert.Equal(HRMS.Application.Common.ResultStatus.Conflict, blocked.Status);

        var proof = await service.AddProofOwnAsync(new TaxDeclarationProofRequest { LineId = lineId, FileName = "pf.pdf", ContentType = "application/pdf", FileSize = 1200, StorageReference = "tax-proof/pf-001", DocumentType = "InvestmentProof" });
        Assert.True(proof.Succeeded, proof.Message);
        var submitted = await service.SubmitOwnAsync();
        Assert.True(submitted.Succeeded, submitted.Message);

        var proofRejected = await service.ReviewProofAsync(submitted.Value!.Id, lineId, proof.Value!.Id, new TaxDeclarationProofReviewRequest { Decision = TaxDeclarationProofStatus.Rejected, Comment = "Upload a clearer proof" });
        Assert.True(proofRejected.Succeeded, proofRejected.Message);
        var requested = await service.RequestResubmissionAsync(submitted.Value.Id, "Please replace the rejected proof.");
        Assert.True(requested.Succeeded, requested.Message);
        var replacement = await service.ReplaceProofOwnAsync(submitted.Value.Id, lineId, proof.Value.Id, new TaxDeclarationProofRequest { LineId = lineId, FileName = "pf-replacement.pdf", ContentType = "application/pdf", FileSize = 1400, StorageReference = "tax-proof/pf-replacement", DocumentType = "InvestmentProof" });
        Assert.True(replacement.Succeeded, replacement.Message);
        var resubmitted = await service.ResubmitOwnAsync(submitted.Value.Id);
        Assert.True(resubmitted.Succeeded, resubmitted.Message);
        var proofReviewed = await service.ReviewProofAsync(submitted.Value.Id, lineId, replacement.Value!.Id, new TaxDeclarationProofReviewRequest { Decision = TaxDeclarationProofStatus.Accepted, Comment = "Replacement accepted" });
        Assert.True(proofReviewed.Succeeded, proofReviewed.Message);

        var reviewed = await service.ReviewLineAsync(submitted.Value!.Id, new TaxDeclarationReviewRequest { LineId = lineId, Decision = EmployeeTaxDeclarationLineStatus.PartiallyApproved, ApprovedAmount = 75_000m, Comment = "Partially supported" });
        Assert.True(reviewed.Succeeded, reviewed.Message);
        var approved = await service.ApproveAsync(submitted.Value.Id);
        Assert.True(approved.Succeeded, approved.Message);

        var resolved = await service.ResolveApprovedAsync(employeeId, 2026, new DateOnly(2026, 9, 30));
        Assert.True(resolved.Succeeded, resolved.Message);
        var input = Assert.Single(resolved.Value!);
        Assert.Equal(75_000m, input.ApprovedAmount);
        Assert.Equal("PF", input.ItemCode);

        var locked = await service.LockAsync(submitted.Value.Id);
        Assert.True(locked.Succeeded, locked.Message);
        var reopened = await service.ReopenAsync(submitted.Value.Id, "Employee correction required");
        Assert.True(reopened.Succeeded, reopened.Message);

        var audit = await service.GetAuditAsync(submitted.Value.Id);
        Assert.True(audit.Succeeded, audit.Message);
        Assert.Contains(audit.Value!, x => x.Action == TaxDeclarationAuditAction.ProofUploaded);
        Assert.Contains(audit.Value!, x => x.Action == TaxDeclarationAuditAction.Submitted);
        Assert.Contains(audit.Value!, x => x.Action == TaxDeclarationAuditAction.Approved);
        Assert.Contains(audit.Value!, x => x.Action == TaxDeclarationAuditAction.Reopened);

        tenant.TenantId = Guid.NewGuid();
        tenant.UserId = Guid.NewGuid();
        await using var isolatedDb = database.CreateContext(tenant);
        var isolatedService = new TaxDeclarationService(isolatedDb, tenant, new EmployeeIdentityResolver(isolatedDb, tenant), TimeProvider.System);
        var isolated = await isolatedService.ResolveApprovedAsync(employeeId, 2026, new DateOnly(2026, 9, 30));
        Assert.True(isolated.Succeeded, isolated.Message);
        Assert.Empty(isolated.Value!);
    }
}
