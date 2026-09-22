using HRMS.Application.DTOs.Payroll;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

internal static class PayrollTaxDeclarationsProviderAcceptance
{
    public static async Task RunAsync(HrmsDbContext db, TestTenantContext tenant, DatabaseProviderType provider)
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var linkId = Guid.NewGuid();
        tenant.TenantId = tenantId;
        tenant.UserId = userId;

        db.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"TD{tenantId:N}"[..12], TenantName = $"Tax declarations {provider}", Host = $"{tenantId:N}.tax.test", ShardKey = tenantId.ToString("N") });
        db.Users.Add(new User { Id = userId, TenantId = tenantId, Email = $"{userId:N}@tax.test", PasswordHash = "provider-test-hash", FirstName = "Tax", LastName = "Reviewer", IsActive = true });
        db.Employees.Add(new Employee { Id = employeeId, TenantId = tenantId, EmployeeCode = "TD-EMP-001", FirstName = "Tax", LastName = "Employee", Email = $"{employeeId:N}@tax.test", DateOfJoining = new(2025, 1, 1), Status = EmployeeStatus.Active });
        db.AccountEmployeeLinkEvents.Add(new AccountEmployeeLinkEvent { Id = linkId, TenantId = tenantId, SubjectUserId = userId, ActorUserId = userId, Sequence = 1, Operation = "Link", NewLinkId = linkId, AfterEmployeeId = employeeId, OccurredAtUtc = DateTime.UtcNow, Reason = "provider acceptance", CorrelationId = linkId.ToString("N") });
        db.AccountEmployeeCurrentLinks.Add(new AccountEmployeeCurrentLink { LinkId = linkId, TenantId = tenantId, UserId = userId, EmployeeId = employeeId });
        await db.SaveChangesAsync();

        var service = new TaxDeclarationService(db, tenant, new EmployeeIdentityResolver(db, tenant), TimeProvider.System);
        var cycle = await service.CreateCycleAsync(new TaxDeclarationCycleRequest { Code = "FY2026", Name = "FY 2026", FinancialYear = 2026, DeclarationOpenDate = new(2026, 4, 1), DeclarationCloseDate = new(2026, 9, 30), ProofSubmissionOpenDate = new(2026, 4, 1), ProofSubmissionCloseDate = new(2026, 10, 31), EffectiveFrom = new(2026, 4, 1), Status = TaxDeclarationCycleStatus.Open });
        Assert.True(cycle.Succeeded, cycle.Message);
        var category = await service.CreateCategoryAsync(new TaxDeclarationCategoryRequest { Code = "INV", Name = "Investment", CategoryType = TaxDeclarationCategoryType.Investment });
        Assert.True(category.Succeeded, category.Message);
        var item = await service.CreateItemAsync(new TaxDeclarationItemRequest { CategoryId = category.Value!.Id, Code = "PF", Name = "Provident Fund", PayrollTaxInputCode = "PF_INPUT" });
        Assert.True(item.Succeeded, item.Message);
        var declaration = await service.CreateOwnAsync(cycle.Value!.Id);
        Assert.True(declaration.Succeeded, declaration.Message);
        var line = await service.AddLineOwnAsync(new TaxDeclarationLineRequest { CategoryId = category.Value.Id, ItemId = item.Value!.Id, DeclaredAmount = 1000m });
        Assert.True(line.Succeeded, line.Message);
        var submitted = await service.SubmitOwnAsync();
        Assert.True(submitted.Succeeded, submitted.Message);
        var reviewed = await service.ReviewLineAsync(submitted.Value!.Id, new TaxDeclarationReviewRequest { LineId = line.Value!.Lines.Single().Id, Decision = EmployeeTaxDeclarationLineStatus.Approved, ApprovedAmount = 800m });
        Assert.True(reviewed.Succeeded, reviewed.Message);
        var approved = await service.ApproveAsync(submitted.Value.Id);
        Assert.True(approved.Succeeded, approved.Message);
        var inputs = await service.ResolveApprovedAsync(employeeId, 2026, new(2026, 9, 30));
        Assert.True(inputs.Succeeded, inputs.Message);
        Assert.Equal(800m, Assert.Single(inputs.Value!).ApprovedAmount);
        var audit = await service.GetAuditAsync(submitted.Value.Id);
        Assert.True(audit.Succeeded, audit.Message);
        Assert.NotEmpty(audit.Value!);
    }
}
