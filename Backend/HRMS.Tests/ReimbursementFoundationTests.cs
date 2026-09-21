using HRMS.Application.DTOs.Payroll;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class ReimbursementFoundationTests
{
    [Fact]
    public async Task Category_policy_and_claim_lifecycle_reconcile_line_totals()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid(); var employeeId = Guid.NewGuid(); await AddTenantAsync(database, tenantId);
        await using var db = database.CreateContext(new TestTenantContext(tenantId));
        db.Employees.Add(new Employee { Id = employeeId, TenantId = tenantId, EmployeeCode = "CLM-001", FirstName = "Claim", LastName = "Employee", Email = "claim@test.local", DateOfJoining = new DateOnly(2025, 1, 1), Status = EmployeeStatus.Active });
        await db.SaveChangesAsync();
        var service = new ReimbursementService(db, new TestTenantContext(tenantId), TimeProvider.System);
        var category = await service.CreateCategoryAsync(new ReimbursementCategoryRequest { Code = "MEAL", Name = "Meals", CategoryType = ReimbursementCategoryType.Meal, CurrencyCode = "INR", AllowsMultipleLines = true });
        Assert.True(category.Succeeded, category.Message);
        var policy = await service.CreatePolicyVersionAsync(category.Value!.Id, new ReimbursementPolicyVersionRequest { EffectiveFrom = new DateOnly(2026, 1, 1), PerTransactionLimit = 400, TaxTreatment = ReimbursementTaxTreatment.NonTaxable });
        Assert.True(policy.Succeeded, policy.Message);
        var claim = await service.CreateClaimAsync(new ReimbursementClaimRequest { EmployeeId = employeeId, ClaimDate = new DateOnly(2026, 9, 21), CurrencyCode = "INR", Lines = [new() { ReimbursementCategoryId = category.Value.Id, ExpenseDate = new DateOnly(2026, 9, 20), Description = "Team meal", ClaimedAmount = 500 }] });
        Assert.True(claim.Succeeded, claim.Message);
        Assert.Equal("CLM/2026/000001", claim.Value!.ClaimNumber);
        var secondClaim = await service.CreateClaimAsync(new ReimbursementClaimRequest { EmployeeId = employeeId, ClaimDate = new DateOnly(2026, 9, 22), CurrencyCode = "INR", Lines = [new() { ReimbursementCategoryId = category.Value.Id, ExpenseDate = new DateOnly(2026, 9, 22), Description = "Second meal", ClaimedAmount = 50 }] });
        Assert.True(secondClaim.Succeeded, secondClaim.Message);
        Assert.Equal("CLM/2026/000002", secondClaim.Value!.ClaimNumber);
        var submitted = await service.SubmitAsync(claim.Value!.Id);
        Assert.True(submitted.Succeeded, submitted.Message);
        var approved = await service.ApproveAsync(claim.Value.Id, new ReimbursementApprovalRequest { Lines = [new() { ClaimLineId = submitted.Value!.Lines[0].Id, ApprovedAmount = 350, Comment = "Within policy" }] });
        Assert.True(approved.Succeeded, approved.Message);
        Assert.Equal(ReimbursementClaimStatus.ReadyForSettlement, approved.Value!.Status);
        Assert.Equal(350m, approved.Value.TotalApprovedAmount);
        Assert.Equal(350m, approved.Value.Lines[0].ApprovedAmount);
        Assert.Equal(350m, approved.Value.NonTaxableAmount);
    }

    [Fact]
    public async Task Category_codes_are_tenant_scoped_and_claims_are_isolated()
    {
        using var database = new SqliteInMemoryDatabase(); var first = Guid.NewGuid(); var second = Guid.NewGuid(); await AddTenantAsync(database, first); await AddTenantAsync(database, second);
        await using var firstDb = database.CreateContext(new TestTenantContext(first)); await using var secondDb = database.CreateContext(new TestTenantContext(second));
        var firstService = new ReimbursementService(firstDb, new TestTenantContext(first), TimeProvider.System); var secondService = new ReimbursementService(secondDb, new TestTenantContext(second), TimeProvider.System);
        Assert.True((await firstService.CreateCategoryAsync(new ReimbursementCategoryRequest { Code = "TRAVEL", Name = "Travel" })).Succeeded);
        Assert.True((await secondService.CreateCategoryAsync(new ReimbursementCategoryRequest { Code = "TRAVEL", Name = "Travel" })).Succeeded);
        Assert.Single((await firstService.GetCategoriesAsync()).Value!); Assert.Single((await secondService.GetCategoriesAsync()).Value!);
    }

    [Fact]
    public async Task Inactive_employee_cannot_submit_reimbursement_claim()
    {
        using var database = new SqliteInMemoryDatabase(); var tenantId = Guid.NewGuid(); var employeeId = Guid.NewGuid(); await AddTenantAsync(database, tenantId);
        await using var db = database.CreateContext(new TestTenantContext(tenantId));
        db.Employees.Add(new Employee { Id = employeeId, TenantId = tenantId, EmployeeCode = "INACTIVE-001", FirstName = "Inactive", LastName = "Employee", Email = "inactive@test.local", DateOfJoining = new DateOnly(2025, 1, 1), Status = EmployeeStatus.Resigned }); await db.SaveChangesAsync();
        var service = new ReimbursementService(db, new TestTenantContext(tenantId), TimeProvider.System);
        var category = await service.CreateCategoryAsync(new ReimbursementCategoryRequest { Code = "OTHER", Name = "Other", CategoryType = ReimbursementCategoryType.Other }); Assert.True(category.Succeeded, category.Message);
        Assert.True((await service.CreatePolicyVersionAsync(category.Value!.Id, new ReimbursementPolicyVersionRequest { EffectiveFrom = new DateOnly(2026, 1, 1) })).Succeeded);
        var claim = await service.CreateClaimAsync(new ReimbursementClaimRequest { EmployeeId = employeeId, ClaimDate = new DateOnly(2026, 9, 21), Lines = [new() { ReimbursementCategoryId = category.Value.Id, ExpenseDate = new DateOnly(2026, 9, 20), Description = "Inactive claim", ClaimedAmount = 10 }] }); Assert.True(claim.Succeeded, claim.Message);
        var submission = await service.SubmitAsync(claim.Value!.Id); Assert.False(submission.Succeeded); Assert.Contains("active", submission.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Receipt_policy_marks_uploaded_metadata_and_preserves_history()
    {
        using var database = new SqliteInMemoryDatabase(); var tenantId = Guid.NewGuid(); var employeeId = Guid.NewGuid(); await AddTenantAsync(database, tenantId);
        await using var db = database.CreateContext(new TestTenantContext(tenantId));
        db.Employees.Add(new Employee { Id = employeeId, TenantId = tenantId, EmployeeCode = "RCPT-001", FirstName = "Receipt", LastName = "Employee", Email = "receipt@test.local", DateOfJoining = new DateOnly(2025, 1, 1), Status = EmployeeStatus.Active }); await db.SaveChangesAsync();
        var tenant = new TestTenantContext(tenantId); var service = new ReimbursementService(db, tenant, TimeProvider.System);
        var category = await service.CreateCategoryAsync(new ReimbursementCategoryRequest { Code = "MED", Name = "Medical", CategoryType = ReimbursementCategoryType.Medical, RequiresReceipt = true }); Assert.True(category.Succeeded, category.Message);
        Assert.True((await service.CreatePolicyVersionAsync(category.Value!.Id, new ReimbursementPolicyVersionRequest { EffectiveFrom = new DateOnly(2026, 1, 1), RequiresReceipt = true })).Succeeded);
        var claim = await service.CreateClaimAsync(new ReimbursementClaimRequest { EmployeeId = employeeId, ClaimDate = new DateOnly(2026, 9, 21), Lines = [new() { ReimbursementCategoryId = category.Value.Id, ExpenseDate = new DateOnly(2026, 9, 20), Description = "Prescription", ClaimedAmount = 100 }] }); Assert.True(claim.Succeeded, claim.Message);
        Assert.False((await service.SubmitAsync(claim.Value!.Id)).Succeeded);
        var attachment = await service.AddAttachmentAsync(claim.Value.Id, new ReimbursementAttachmentRequest { ClaimLineId = claim.Value.Lines[0].Id, FileName = "receipt.pdf", ContentType = "application/pdf", StorageReference = "claims/receipt-1", FileSize = 1234 }); Assert.True(attachment.Succeeded, attachment.Message);
        var submitted = await service.SubmitAsync(claim.Value.Id); Assert.True(submitted.Succeeded, submitted.Message); Assert.Equal(ReimbursementReceiptStatus.Uploaded, submitted.Value!.Lines[0].ReceiptStatus);
        var history = await service.GetHistoryAsync(claim.Value.Id); Assert.True(history.Succeeded); Assert.Contains(history.Value!, x => x.EventType == ReimbursementHistoryEventType.Submitted);
    }

    [Fact]
    public async Task Register_pages_one_hundred_claims_and_reconciles_totals()
    {
        using var database = new SqliteInMemoryDatabase(); var tenantId = Guid.NewGuid(); var employeeId = Guid.NewGuid(); await AddTenantAsync(database, tenantId);
        await using var db = database.CreateContext(new TestTenantContext(tenantId));
        db.Employees.Add(new Employee { Id = employeeId, TenantId = tenantId, EmployeeCode = "PAGE-001", FirstName = "Page", LastName = "Employee", Email = "page@test.local", DateOfJoining = new DateOnly(2025, 1, 1), Status = EmployeeStatus.Active }); await db.SaveChangesAsync();
        var tenant = new TestTenantContext(tenantId); var service = new ReimbursementService(db, tenant, TimeProvider.System);
        var category = await service.CreateCategoryAsync(new ReimbursementCategoryRequest { Code = "FUEL", Name = "Fuel", CategoryType = ReimbursementCategoryType.Fuel }); Assert.True(category.Succeeded, category.Message);
        Assert.True((await service.CreatePolicyVersionAsync(category.Value!.Id, new ReimbursementPolicyVersionRequest { EffectiveFrom = new DateOnly(2026, 1, 1), PerTransactionLimit = 1000 })).Succeeded);
        var createdClaims = new List<ReimbursementClaimDto>(); for (var index = 0; index < 100; index++) { var date = new DateOnly(2026, 9, 1).AddDays(index % 20); var lines = new List<ReimbursementClaimLineRequest> { new() { ReimbursementCategoryId = category.Value.Id, ExpenseDate = date, Description = $"Fuel {index}-1", ClaimedAmount = 10 }, new() { ReimbursementCategoryId = category.Value.Id, ExpenseDate = date, Description = $"Fuel {index}-2", ClaimedAmount = 10 } }; if (index < 50) lines.Add(new() { ReimbursementCategoryId = category.Value.Id, ExpenseDate = date, Description = $"Fuel {index}-3", ClaimedAmount = 10 }); var created = await service.CreateClaimAsync(new ReimbursementClaimRequest { EmployeeId = employeeId, ClaimDate = date, Lines = lines }); Assert.True(created.Succeeded, created.Message); createdClaims.Add(created.Value!); }
        foreach (var created in createdClaims) { Assert.True((await service.SubmitAsync(created.Id)).Succeeded); var approval = await service.ApproveAsync(created.Id, new ReimbursementApprovalRequest { Lines = created.Lines.Select(line => new ReimbursementApprovalLineRequest { ClaimLineId = line.Id, ApprovedAmount = line.ClaimedAmount }).ToList() }); Assert.True(approval.Succeeded, approval.Message); }
        foreach (var created in createdClaims.Take(50)) { var settled = await service.SettleManuallyAsync(created.Id, new ReimbursementSettlementRequest { Reference = $"large-{created.Id:N}" }); Assert.True(settled.Succeeded, settled.Message); }
        var first = await service.GetRegisterAsync(new ReimbursementClaimQuery { Page = 1, PageSize = 50 }); var second = await service.GetRegisterAsync(new ReimbursementClaimQuery { Page = 2, PageSize = 50 });
        var allRows = first.Value!.Items.Concat(second.Value!.Items).ToList(); Assert.True(first.Succeeded, first.Message); Assert.True(second.Succeeded, second.Message); Assert.Equal(100, first.Value.TotalCount); Assert.Equal(50, first.Value.Items.Count); Assert.Equal(50, second.Value.Items.Count); Assert.Equal(2500m, allRows.Sum(x => x.ClaimedAmount)); Assert.Equal(2500m, allRows.Sum(x => x.EligibleAmount)); Assert.Equal(2500m, allRows.Sum(x => x.ApprovedAmount)); Assert.Equal(0m, allRows.Sum(x => x.TaxableAmount)); Assert.Equal(2500m, allRows.Sum(x => x.NonTaxableAmount)); Assert.Equal(1500m, allRows.Sum(x => x.SettledAmount)); Assert.Equal(1000m, allRows.Sum(x => x.OutstandingAmount)); Assert.Equal(250, await db.ReimbursementClaimLines.CountAsync(x => x.TenantId == tenantId));
        tenant.TenantId = Guid.NewGuid(); Assert.Empty((await service.GetRegisterAsync(new ReimbursementClaimQuery { Page = 1, PageSize = 50 })).Value!.Items);
    }

    private static async Task AddTenantAsync(SqliteInMemoryDatabase database, Guid tenantId)
    {
        await using var catalog = database.CreateContext(new TestTenantContext()); catalog.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"CL{tenantId:N}"[..12], TenantName = "Claims tenant", Host = $"{tenantId:N}.claims.test", ShardKey = tenantId.ToString("N") }); await catalog.SaveChangesAsync();
    }
}
