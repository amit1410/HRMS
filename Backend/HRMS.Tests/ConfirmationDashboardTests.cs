using System.Linq.Expressions;
using HRMS.Application.Abstractions;
using HRMS.Application.DTOs.Confirmation;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class ConfirmationDashboardTests
{
    [Fact]
    public async Task Dashboard_pending_counts_follow_approval_requirement_and_sequence()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenant = Guid.NewGuid();
        var employee = Guid.NewGuid();
        var db = database.CreateContext(new TestTenantContext(tenant));
        await SeedTenantAndEmployeeAsync(db, tenant, employee);

        await AddCaseAsync(db, tenant, employee, ConfirmationApprovalRequirement.Manager);
        await AddCaseAsync(db, tenant, employee, ConfirmationApprovalRequirement.Hrbp);
        await AddCaseAsync(db, tenant, employee, ConfirmationApprovalRequirement.ManagerOrHrbp);
        var parallelBoth = await AddCaseAsync(db, tenant, employee, ConfirmationApprovalRequirement.ManagerAndHrbp);
        await AddReviewAsync(db, tenant, parallelBoth, ConfirmationReviewerType.ReportingManager, 1);
        var sequentialBoth = await AddCaseAsync(db, tenant, employee, ConfirmationApprovalRequirement.ManagerAndHrbp, ApprovalSequence.ManagerThenHrbp);

        var service = new HrConfirmationDashboardService(db, new TestTenantContext(tenant), new AllowListScope(employee));
        var result = await service.GetSummaryAsync(new HrConfirmationDashboardFilter());

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(3, result.Value!.Kpis.PendingManagerActions); // Manager, ManagerOrHrbp, sequential both
        Assert.Equal(3, result.Value.Kpis.PendingHrbpActions); // HRBP, ManagerOrHrbp, and parallel both; sequential HRBP waits for Manager
        Assert.Equal(6, result.Value.PendingActions.Count);
    }

    [Fact]
    public async Task Dashboard_applies_tenant_and_employee_access_scope()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var employeeA = Guid.NewGuid();
        var employeeB = Guid.NewGuid();
        var db = database.CreateContext(new TestTenantContext(tenantA));
        await SeedTenantAndEmployeeAsync(db, tenantA, employeeA);
        await SeedTenantAndEmployeeAsync(db, tenantA, employeeB);
        await SeedTenantAndEmployeeAsync(db, tenantB, Guid.NewGuid());
        await AddCaseAsync(db, tenantA, employeeA, ConfirmationApprovalRequirement.Manager);
        await AddCaseAsync(db, tenantA, employeeB, ConfirmationApprovalRequirement.Manager);
        await AddCaseAsync(db, tenantB, employeeB, ConfirmationApprovalRequirement.Manager);

        var result = await new HrConfirmationDashboardService(
            db, new TestTenantContext(tenantA), new AllowListScope(employeeA)).GetSummaryAsync(new HrConfirmationDashboardFilter());

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(1, result.Value!.Kpis.ProbationEmployees);
        Assert.Equal(1, result.Value.Kpis.PendingManagerActions);
    }

    private static async Task SeedTenantAndEmployeeAsync(HrmsDbContext db, Guid tenant, Guid employee)
    {
        if (!await db.Tenants.AnyAsync(x => x.Id == tenant))
            db.Tenants.Add(new Tenant { Id = tenant, TenantCode = tenant.ToString("N")[..8], Host = $"{tenant:N}.local", ShardKey = tenant.ToString("N"), TenantName = "Test" });
        db.Employees.Add(new Employee { Id = employee, TenantId = tenant, EmployeeCode = employee.ToString("N")[..8], FirstName = "Test", LastName = "Employee", Email = $"{employee:N}@test.local", DateOfJoining = new DateOnly(2026, 1, 1), Status = EmployeeStatus.Active });
        await db.SaveChangesAsync();
    }

    private static async Task<Guid> AddCaseAsync(HrmsDbContext db, Guid tenant, Guid employee, ConfirmationApprovalRequirement requirement, ApprovalSequence sequence = ApprovalSequence.Parallel)
    {
        var policyId = Guid.NewGuid();
        db.ConfirmationPolicies.Add(new ConfirmationPolicy { Id = policyId, TenantId = tenant, Code = policyId.ToString("N")[..8], Name = "Dashboard policy", IsActive = true });
        var version = new ConfirmationPolicyVersion { Id = Guid.NewGuid(), TenantId = tenant, ConfirmationPolicyId = policyId, VersionNumber = 1, EffectiveFrom = new DateOnly(2026, 1, 1), Status = ConfirmationPolicyVersionStatus.Published };
        db.ConfirmationPolicyVersions.Add(version);
        db.ConfirmationPolicyRules.Add(new ConfirmationPolicyRule { Id = Guid.NewGuid(), TenantId = tenant, ConfirmationPolicyVersionId = version.Id, RequiredApproval = requirement, ApprovalSequence = sequence, ProbationDuration = 6, TriggerDaysBeforeEnd = 30 });
        var id = Guid.NewGuid();
        db.ConfirmationReviewCases.Add(new ConfirmationReviewCase
        {
            Id = id, TenantId = tenant, EmployeeId = employee, ConfirmationPolicyId = version.ConfirmationPolicyId, ConfirmationPolicyVersionId = version.Id,
            ProbationCycleKey = id.ToString("N"), ReviewRound = 1, CycleNumber = 1, ProbationStartDate = new DateOnly(2026, 1, 1), ProbationEndDate = new DateOnly(2026, 12, 31), DueDate = new DateOnly(2026, 10, 1), Status = ConfirmationReviewCaseStatus.InReview
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task AddReviewAsync(HrmsDbContext db, Guid tenant, Guid caseId, ConfirmationReviewerType reviewerType, int round)
    {
        db.ConfirmationReviewerReviews.Add(new ConfirmationReviewerReview { Id = Guid.NewGuid(), TenantId = tenant, ConfirmationReviewCaseId = caseId, ReviewRound = round, ReviewerType = reviewerType, Status = ConfirmationReviewStatus.Submitted, SubmittedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }

    private sealed class AllowListScope(Guid employeeId) : IEmployeeAccessScopeService
    {
        private readonly Guid allowedEmployeeId = employeeId;
        public Task<Expression<Func<Employee, bool>>> BuildPredicateAsync(DateOnly effectiveDate, CancellationToken cancellationToken = default) => Task.FromResult<Expression<Func<Employee, bool>>>(x => x.Id == employeeId);
        public Task<Expression<Func<Employee, bool>>> BuildRoleScopePredicateAsync(DateOnly effectiveDate, CancellationToken cancellationToken = default) => BuildPredicateAsync(effectiveDate, cancellationToken);
        public Task<bool> CanAccessEmployeeAsync(Guid employeeId, DateOnly effectiveDate, CancellationToken cancellationToken = default) => Task.FromResult(employeeId == allowedEmployeeId);
    }
}
