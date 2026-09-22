using HRMS.Application.DTOs.Payroll;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;

namespace HRMS.Tests;

public sealed class TaxDeclarationLargeDataTests
{
    [Fact]
    public async Task Large_declaration_queue_and_approved_resolver_are_paged_and_tenant_scoped()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var tenant = new TestTenantContext(tenantId, userId);
        var cycleId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var approvedEmployeeId = Guid.Empty;

        await using (var seed = database.CreateContext(tenant))
        {
            seed.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"LD{tenantId:N}"[..10], TenantName = "Large declaration tenant", Host = $"{tenantId:N}.large.test", ShardKey = tenantId.ToString("N") });
            seed.Users.Add(new User { Id = userId, TenantId = tenantId, Email = $"{userId:N}@large.test", PasswordHash = "test-hash", FirstName = "Large", LastName = "Test" });
            seed.TaxDeclarationCycles.Add(new TaxDeclarationCycle { Id = cycleId, TenantId = tenantId, Code = "FY2026", Name = "FY 2026", FinancialYear = 2026, DeclarationOpenDate = new(2026, 4, 1), DeclarationCloseDate = new(2026, 9, 30), ProofSubmissionOpenDate = new(2026, 4, 1), ProofSubmissionCloseDate = new(2026, 10, 31), EffectiveFrom = new(2026, 4, 1), Status = TaxDeclarationCycleStatus.Open });
            seed.TaxDeclarationCategories.Add(new TaxDeclarationCategory { Id = categoryId, TenantId = tenantId, Code = "INV", Name = "Investment", CategoryType = TaxDeclarationCategoryType.Investment });
            seed.TaxDeclarationItems.Add(new TaxDeclarationItem { Id = itemId, TenantId = tenantId, TaxDeclarationCategoryId = categoryId, Code = "PF", Name = "Provident Fund", PayrollTaxInputCode = "PF_INPUT" });
            var employees = Enumerable.Range(0, 1000).Select(i => new Employee { Id = i == 0 ? employeeId : Guid.NewGuid(), TenantId = tenantId, EmployeeCode = $"LD-{i:0000}", FirstName = "Employee", LastName = i.ToString(), Email = $"ld-{i}@large.test", DateOfJoining = new(2025, 1, 1), Status = EmployeeStatus.Active }).ToList();
            seed.Employees.AddRange(employees);
            var first = employees[0];
            var linkId = Guid.NewGuid();
            seed.AccountEmployeeLinkEvents.Add(new AccountEmployeeLinkEvent { Id = linkId, TenantId = tenantId, SubjectUserId = userId, ActorUserId = userId, Sequence = 1, Operation = "Link", NewLinkId = linkId, AfterEmployeeId = first.Id, OccurredAtUtc = DateTime.UtcNow, Reason = "large test", CorrelationId = linkId.ToString("N") });
            seed.AccountEmployeeCurrentLinks.Add(new AccountEmployeeCurrentLink { LinkId = linkId, TenantId = tenantId, UserId = userId, EmployeeId = first.Id });
            await seed.SaveChangesAsync();

            employees = employees.OrderBy(x => x.EmployeeCode).ToList();
            approvedEmployeeId = employees[4].Id;
            var statuses = new[] { EmployeeTaxDeclarationStatus.Draft, EmployeeTaxDeclarationStatus.Submitted, EmployeeTaxDeclarationStatus.UnderReview, EmployeeTaxDeclarationStatus.PartiallyApproved, EmployeeTaxDeclarationStatus.Approved, EmployeeTaxDeclarationStatus.Rejected, EmployeeTaxDeclarationStatus.ResubmissionRequired, EmployeeTaxDeclarationStatus.Locked };
            var declarations = employees.Select((employee, index) => new EmployeeTaxDeclaration { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employee.Id, TaxDeclarationCycleId = cycleId, Status = statuses[index % statuses.Length], Version = index % 3 + 1 }).ToList();
            seed.EmployeeTaxDeclarations.AddRange(declarations);
            seed.EmployeeTaxDeclarationLines.AddRange(declarations.Select((declaration, index) => new EmployeeTaxDeclarationLine { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeTaxDeclarationId = declaration.Id, TaxDeclarationCategoryId = categoryId, TaxDeclarationItemId = itemId, DeclaredAmount = 1000m + index, ApprovedAmount = declaration.Status is EmployeeTaxDeclarationStatus.Approved or EmployeeTaxDeclarationStatus.PartiallyApproved or EmployeeTaxDeclarationStatus.Locked ? 750m : declaration.Status == EmployeeTaxDeclarationStatus.Rejected ? 0m : null, Status = declaration.Status is EmployeeTaxDeclarationStatus.Approved or EmployeeTaxDeclarationStatus.Locked ? EmployeeTaxDeclarationLineStatus.Approved : declaration.Status == EmployeeTaxDeclarationStatus.PartiallyApproved ? EmployeeTaxDeclarationLineStatus.PartiallyApproved : declaration.Status == EmployeeTaxDeclarationStatus.Rejected ? EmployeeTaxDeclarationLineStatus.Rejected : EmployeeTaxDeclarationLineStatus.Submitted }));
            await seed.SaveChangesAsync();
            var lines = seed.EmployeeTaxDeclarationLines.OrderBy(x => x.CreatedDate).Take(100).ToList();
            seed.TaxDeclarationProofs.AddRange(lines.Select(line => new TaxDeclarationProof { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeTaxDeclarationLineId = line.Id, FileName = "proof.pdf", ContentType = "application/pdf", FileSize = 10, StorageReference = $"proofs/{line.Id:N}", UploadedByUserId = userId }));
            await seed.SaveChangesAsync();
        }

        await using var db = database.CreateContext(tenant);
        var service = new TaxDeclarationService(db, tenant, new EmployeeIdentityResolver(db, tenant), TimeProvider.System);
        var page1 = await service.GetReviewAsync(new TaxDeclarationReviewQuery { CycleId = cycleId, Page = 1, PageSize = 50 });
        var page2 = await service.GetReviewAsync(new TaxDeclarationReviewQuery { CycleId = cycleId, Page = 2, PageSize = 50 });
        Assert.True(page1.Succeeded, page1.Message);
        Assert.True(page2.Succeeded, page2.Message);
        Assert.Equal(1000, page1.Value!.TotalCount);
        Assert.Equal(50, page1.Value.Items.Count);
        Assert.Equal(50, page2.Value!.Items.Count);
        Assert.NotEqual(page1.Value.Items[0].Id, page2.Value.Items[0].Id);

        var approved = await service.ResolveApprovedAsync(approvedEmployeeId, 2026, new(2026, 9, 30));
        Assert.True(approved.Succeeded, approved.Message);
        Assert.Single(approved.Value!);
        Assert.Equal(750m, approved.Value![0].ApprovedAmount);

        tenant.TenantId = Guid.NewGuid();
        tenant.UserId = Guid.NewGuid();
        await using var isolatedDb = database.CreateContext(tenant);
        var isolatedService = new TaxDeclarationService(isolatedDb, tenant, new EmployeeIdentityResolver(isolatedDb, tenant), TimeProvider.System);
        var isolated = await isolatedService.GetReviewAsync(new TaxDeclarationReviewQuery { Page = 1, PageSize = 50 });
        Assert.True(isolated.Succeeded, isolated.Message);
        Assert.Equal(0, isolated.Value!.TotalCount);
    }
}
