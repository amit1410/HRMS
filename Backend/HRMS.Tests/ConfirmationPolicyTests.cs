using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Confirmation;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

/// <summary>
/// Phase 9a — Confirmation &amp; Probation policy foundation: date arithmetic, policy resolution
/// (priority → specificity → effective date), CRUD/publish lifecycle, and the employee confirmation
/// status with its no-policy manual fallback.
/// </summary>
public sealed class ConfirmationPolicyTests
{
    private static readonly DateOnly Doj = new(2026, 1, 15);

    // ── Calculator ────────────────────────────────────────────────────────────

    [Fact]
    public void Probation_end_adds_months_and_days()
    {
        Assert.Equal(new DateOnly(2026, 7, 15), ConfirmationDateCalculator.ProbationEndDate(Doj, 6, ProbationDurationUnit.Months));
        Assert.Equal(new DateOnly(2026, 2, 14), ConfirmationDateCalculator.ProbationEndDate(Doj, 30, ProbationDurationUnit.Days));
    }

    [Fact]
    public void Review_trigger_date_is_window_before_end_and_never_before_joining()
    {
        var end = new DateOnly(2026, 7, 15);
        Assert.Equal(new DateOnly(2026, 6, 15), ConfirmationDateCalculator.ReviewTriggerDate(Doj, end, 30));
        // A huge window clamps to the date of joining rather than going earlier.
        Assert.Equal(Doj, ConfirmationDateCalculator.ReviewTriggerDate(Doj, end, 9999));
    }

    // ── Resolver ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Resolver_returns_no_policy_when_none_published()
    {
        using var db = new SqliteInMemoryDatabase();
        var tenant = Guid.NewGuid(); var employee = Guid.NewGuid();
        using var context = db.CreateContext(new TestTenantContext(tenant));
        await AddEmployeeAsync(context, tenant, employee);

        var result = await new ConfirmationPolicyResolver(context).ResolveAsync(tenant, employee, new(2026, 6, 1));

        Assert.Equal(ConfirmationPolicyResolutionStatus.NoPolicy, result.Status);
    }

    [Fact]
    public async Task Resolver_applies_tenant_wide_default_and_computes_probation_end()
    {
        using var db = new SqliteInMemoryDatabase();
        var tenant = Guid.NewGuid(); var employee = Guid.NewGuid();
        using var context = db.CreateContext(new TestTenantContext(tenant));
        await AddEmployeeAsync(context, tenant, employee);
        await AddPublishedPolicyAsync(context, tenant, priority: 1, probationMonths: 6);

        var result = await new ConfirmationPolicyResolver(context).ResolveAsync(tenant, employee, new(2026, 6, 1));

        Assert.Equal(ConfirmationPolicyResolutionStatus.Resolved, result.Status);
        Assert.Equal(0, result.Specificity);
        Assert.Equal(new DateOnly(2026, 7, 15), result.ProbationEndDate);
    }

    [Fact]
    public async Task Resolver_prefers_higher_priority_then_greater_specificity()
    {
        using var db = new SqliteInMemoryDatabase();
        var tenant = Guid.NewGuid(); var employee = Guid.NewGuid(); var department = Guid.NewGuid();
        using var context = db.CreateContext(new TestTenantContext(tenant));
        await AddEmployeeAsync(context, tenant, employee, department);

        // Tenant-wide default at priority 1; a department-specific policy at the same priority wins on specificity.
        await AddPublishedPolicyAsync(context, tenant, priority: 1, probationMonths: 3);
        var specific = await AddPublishedPolicyAsync(context, tenant, priority: 1, probationMonths: 12, departmentId: department);

        var result = await new ConfirmationPolicyResolver(context).ResolveAsync(tenant, employee, new(2026, 6, 1));

        Assert.Equal(ConfirmationPolicyResolutionStatus.Resolved, result.Status);
        Assert.Equal(specific, result.ConfirmationPolicyVersionId);
        Assert.Equal(1, result.Specificity);
        Assert.Equal(12, result.ProbationDuration);
    }

    [Fact]
    public async Task Resolver_reports_ambiguity_for_equal_priority_and_specificity()
    {
        using var db = new SqliteInMemoryDatabase();
        var tenant = Guid.NewGuid(); var employee = Guid.NewGuid();
        using var context = db.CreateContext(new TestTenantContext(tenant));
        await AddEmployeeAsync(context, tenant, employee);
        await AddPublishedPolicyAsync(context, tenant, priority: 5, probationMonths: 3);
        await AddPublishedPolicyAsync(context, tenant, priority: 5, probationMonths: 6);

        var result = await new ConfirmationPolicyResolver(context).ResolveAsync(tenant, employee, new(2026, 6, 1));

        Assert.Equal(ConfirmationPolicyResolutionStatus.ConfigurationAmbiguity, result.Status);
    }

    // ── Service ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Service_create_rejects_duplicate_code()
    {
        using var db = new SqliteInMemoryDatabase();
        var tenant = Guid.NewGuid();
        using var context = db.CreateContext(new TestTenantContext(tenant));
        context.Tenants.Add(NewTenant(tenant)); await context.SaveChangesAsync();
        var service = new ConfirmationPolicyService(context, new TestTenantContext(tenant));

        Assert.Equal(ResultStatus.Success, (await service.CreatePolicyAsync(new("STD", "Standard", null))).Status);
        Assert.Equal(ResultStatus.Conflict, (await service.CreatePolicyAsync(new("std", "Dup", null))).Status);
    }

    [Fact]
    public async Task Service_version_lifecycle_create_publish_and_resolve()
    {
        using var db = new SqliteInMemoryDatabase();
        var tenant = Guid.NewGuid(); var employee = Guid.NewGuid();
        using var context = db.CreateContext(new TestTenantContext(tenant));
        await AddEmployeeAsync(context, tenant, employee);
        var tenantContext = new TestTenantContext(tenant);
        var service = new ConfirmationPolicyService(context, tenantContext);

        var policy = await service.CreatePolicyAsync(new("STD", "Standard", "desc"));
        Assert.Equal(ResultStatus.Success, policy.Status);

        var version = await service.CreateVersionAsync(policy.Value!.Id, new ConfirmationPolicyVersionRequest(
            new(2026, 1, 1), null, 1, new ConfirmationProbationConfigRequest(ProbationDuration: 6)));
        Assert.Equal(ResultStatus.Success, version.Status);

        var published = await service.PublishVersionAsync(policy.Value.Id, version.Value!.Version.Id);
        Assert.Equal(ResultStatus.Success, published.Status);

        var resolve = await service.ResolveAsync(new ConfirmationPolicyResolveRequest(employee, new(2026, 6, 1)));
        Assert.Equal(ResultStatus.Success, resolve.Status);
        Assert.Equal(ConfirmationPolicyResolutionStatus.Resolved, resolve.Value!.Status);
        Assert.Equal(new DateOnly(2026, 7, 15), resolve.Value.ProbationEndDate);
    }

    [Fact]
    public async Task Service_employee_status_falls_back_when_no_policy()
    {
        using var db = new SqliteInMemoryDatabase();
        var tenant = Guid.NewGuid(); var employee = Guid.NewGuid();
        using var context = db.CreateContext(new TestTenantContext(tenant));
        await AddEmployeeAsync(context, tenant, employee);
        var service = new ConfirmationPolicyService(context, new TestTenantContext(tenant));

        var status = await service.GetEmployeeStatusAsync(employee);

        Assert.Equal(ResultStatus.Success, status.Status);
        Assert.False(status.Value!.IsPolicyManaged);
        Assert.Null(status.Value.ProbationEndDate);
    }

    [Fact]
    public async Task Service_employee_status_is_policy_managed_when_resolved()
    {
        using var db = new SqliteInMemoryDatabase();
        var tenant = Guid.NewGuid(); var employee = Guid.NewGuid();
        using var context = db.CreateContext(new TestTenantContext(tenant));
        await AddEmployeeAsync(context, tenant, employee);
        await AddPublishedPolicyAsync(context, tenant, priority: 1, probationMonths: 6);
        var service = new ConfirmationPolicyService(context, new TestTenantContext(tenant));

        var status = await service.GetEmployeeStatusAsync(employee);

        Assert.Equal(ResultStatus.Success, status.Status);
        Assert.True(status.Value!.IsPolicyManaged);
        Assert.Equal(new DateOnly(2026, 7, 15), status.Value.ProbationEndDate);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Tenant NewTenant(Guid tenant) => new()
    {
        Id = tenant, TenantCode = Guid.NewGuid().ToString("N")[..8], Host = Guid.NewGuid().ToString("N") + ".local",
        ShardKey = Guid.NewGuid().ToString("N"), TenantName = "Test"
    };

    private static async Task AddEmployeeAsync(HrmsDbContext db, Guid tenant, Guid employee, Guid? department = null)
    {
        db.Tenants.Add(NewTenant(tenant));
        db.Employees.Add(new Employee { Id = employee, TenantId = tenant, FirstName = "Test", LastName = "Employee", Email = Guid.NewGuid() + "@test.local", DateOfJoining = Doj, Gender = Gender.Unspecified });
        if (department is Guid dept)
            db.Departments.Add(new Department { Id = dept, TenantId = tenant, Code = "D" + Guid.NewGuid().ToString("N")[..6], Name = "Dept" });
        db.EmployeeEmploymentHistory.Add(new EmployeeEmploymentHistory { Id = Guid.NewGuid(), TenantId = tenant, EmployeeId = employee, EffectiveFrom = Doj, DepartmentId = department });
        await db.SaveChangesAsync();
    }

    /// <summary>Creates a published policy/version/rule (+ optional department applicability) and returns the version id.</summary>
    private static async Task<Guid> AddPublishedPolicyAsync(HrmsDbContext db, Guid tenant, int priority, int probationMonths, Guid? departmentId = null)
    {
        var policy = new ConfirmationPolicy { Id = Guid.NewGuid(), TenantId = tenant, Code = Guid.NewGuid().ToString("N")[..8], Name = "Policy", IsActive = true };
        var version = new ConfirmationPolicyVersion { Id = Guid.NewGuid(), TenantId = tenant, ConfirmationPolicyId = policy.Id, VersionNumber = 1, EffectiveFrom = new(2026, 1, 1), Status = ConfirmationPolicyVersionStatus.Published, Priority = priority };
        var rule = new ConfirmationPolicyRule { Id = Guid.NewGuid(), TenantId = tenant, ConfirmationPolicyVersionId = version.Id, ProbationDuration = probationMonths, ProbationDurationUnit = ProbationDurationUnit.Months, TriggerDaysBeforeEnd = 30 };
        db.ConfirmationPolicies.Add(policy);
        db.ConfirmationPolicyVersions.Add(version);
        db.ConfirmationPolicyRules.Add(rule);
        if (departmentId is Guid dept)
            db.ConfirmationPolicyApplicabilitySets.Add(new ConfirmationPolicyApplicabilitySet { Id = Guid.NewGuid(), TenantId = tenant, ConfirmationPolicyVersionId = version.Id, DepartmentId = dept });
        await db.SaveChangesAsync();
        return version.Id;
    }
}
