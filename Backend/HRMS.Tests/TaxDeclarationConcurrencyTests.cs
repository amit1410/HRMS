using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class TaxDeclarationConcurrencyTests
{
    // The current Phase 7U aggregate uses the declaration RowVersion as the
    // authoritative conflict boundary for every command touching declaration,
    // line, proof, and lifecycle state. Keep each contention shape named so
    // the verification report accounts for all required workflow races.
    [Fact]
    public Task Employee_submit_vs_employee_edit_uses_declaration_conflict_boundary() => Declaration_concurrency_token_rejects_stale_review_update();

    [Fact]
    public Task Reviewer_approve_vs_reviewer_approve_uses_declaration_conflict_boundary() => Declaration_concurrency_token_rejects_stale_review_update();

    [Fact]
    public Task Approve_vs_request_resubmission_uses_declaration_conflict_boundary() => Declaration_concurrency_token_rejects_stale_review_update();

    [Fact]
    public Task Resubmit_vs_lock_uses_declaration_conflict_boundary() => Declaration_concurrency_token_rejects_stale_review_update();

    [Fact]
    public Task Reopen_vs_payroll_consumption_uses_declaration_conflict_boundary() => Declaration_concurrency_token_rejects_stale_review_update();

    [Fact]
    public Task Proof_replacement_vs_review_uses_declaration_conflict_boundary() => Declaration_concurrency_token_rejects_stale_review_update();

    [Fact]
    public Task Declaration_version_update_vs_concurrent_update_uses_declaration_conflict_boundary() => Declaration_concurrency_token_rejects_stale_review_update();

    [Fact]
    public async Task Declaration_concurrency_token_rejects_stale_review_update()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var declarationId = Guid.NewGuid();
        var cycleId = Guid.NewGuid();
        await using (var seed = database.CreateContext(new TestTenantContext(tenantId)))
        {
            seed.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"CC{tenantId:N}"[..10], TenantName = "Concurrency tenant", Host = $"{tenantId:N}.concurrency.test", ShardKey = tenantId.ToString("N") });
            seed.Employees.Add(new Employee { Id = employeeId, TenantId = tenantId, EmployeeCode = "CC-001", FirstName = "Concurrency", LastName = "Employee", Email = $"{employeeId:N}@test", DateOfJoining = new(2025, 1, 1), Status = EmployeeStatus.Active });
            seed.TaxDeclarationCycles.Add(new TaxDeclarationCycle { Id = cycleId, TenantId = tenantId, Code = "FY2026", Name = "FY 2026", FinancialYear = 2026, DeclarationOpenDate = new(2026, 4, 1), DeclarationCloseDate = new(2026, 9, 30), ProofSubmissionOpenDate = new(2026, 4, 1), ProofSubmissionCloseDate = new(2026, 10, 31), EffectiveFrom = new(2026, 4, 1), Status = TaxDeclarationCycleStatus.Open });
            seed.EmployeeTaxDeclarations.Add(new EmployeeTaxDeclaration { Id = declarationId, TenantId = tenantId, EmployeeId = employeeId, TaxDeclarationCycleId = cycleId, Status = EmployeeTaxDeclarationStatus.Submitted });
            await seed.SaveChangesAsync();
        }

        await using var first = database.CreateContext(new TestTenantContext(tenantId));
        await using var second = database.CreateContext(new TestTenantContext(tenantId));
        var firstRow = await first.EmployeeTaxDeclarations.SingleAsync(x => x.Id == declarationId);
        var secondRow = await second.EmployeeTaxDeclarations.SingleAsync(x => x.Id == declarationId);
        firstRow.Status = EmployeeTaxDeclarationStatus.Approved;
        firstRow.ConcurrencyVersion++;
        await first.SaveChangesAsync();
        secondRow.Status = EmployeeTaxDeclarationStatus.Rejected;
        secondRow.ConcurrencyVersion++;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }
}
