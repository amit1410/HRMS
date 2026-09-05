using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class RuntimeLeavePolicyEvaluatorFoundationTests
{
    [Fact]
    public async Task Current_identity_uses_the_linked_employee()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenant = Guid.NewGuid();
        var user = Guid.NewGuid();
        var employee = Guid.NewGuid();
        var tenantContext = new TestTenantContext(tenant, user);
        using var context = database.CreateContext(tenantContext);
        AddTenant(context, tenant);
        context.Users.Add(new User
        {
            Id = user,
            TenantId = tenant,
            Email = $"{user}@test.local",
            PasswordHash = "test",
            FirstName = "Test",
            LastName = "User"
        });
        context.Employees.Add(NewEmployee(tenant, employee));
        var linkId = Guid.NewGuid();
        context.AccountEmployeeLinkEvents.Add(new()
        {
            Id = linkId,
            TenantId = tenant,
            SubjectUserId = user,
            ActorUserId = user,
            Sequence = 1,
            Operation = "Link",
            NewLinkId = linkId,
            AfterEmployeeId = employee,
            OccurredAtUtc = DateTime.UtcNow,
            Reason = "Initial test link",
            CorrelationId = "runtime-identity-test"
        });
        context.AccountEmployeeCurrentLinks.Add(new() { LinkId = linkId, TenantId = tenant, UserId = user, EmployeeId = employee });
        await context.SaveChangesAsync();

        var result = await new EmployeeIdentityResolver(context, tenantContext).ResolveCurrentAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(employee, result.Value!.EmployeeId);
        Assert.NotEqual(user, result.Value.EmployeeId);
    }

    [Fact]
    public async Task Current_identity_rejects_an_unlinked_account_without_fallback()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenant = Guid.NewGuid();
        var user = Guid.NewGuid();
        var tenantContext = new TestTenantContext(tenant, user);
        using var context = database.CreateContext(tenantContext);
        AddTenant(context, tenant);
        context.Employees.Add(NewEmployee(tenant, user));
        await context.SaveChangesAsync();

        var result = await new EmployeeIdentityResolver(context, tenantContext).ResolveCurrentAsync();

        Assert.False(result.Succeeded);
        Assert.Equal(ResultStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task Effective_employment_uses_inclusive_boundaries_and_snapshot_ids()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenant = Guid.NewGuid(); var employee = Guid.NewGuid(); var department = Guid.NewGuid();
        using var context = database.CreateContext(new TestTenantContext(tenant));
        AddTenant(context, tenant);
        context.Employees.Add(NewEmployee(tenant, employee));
        context.Departments.Add(new() { Id = department, TenantId = tenant, Code = "IT", Name = "IT" });
        context.EmployeeEmploymentHistory.Add(new()
        {
            Id = Guid.NewGuid(), TenantId = tenant, EmployeeId = employee,
            EffectiveFrom = new(2027, 1, 1), EffectiveTo = new(2027, 12, 31), DepartmentId = department
        });
        await context.SaveChangesAsync();

        var resolver = new EffectiveEmploymentResolver(context);
        var start = await resolver.ResolveAsync(tenant, employee, new(2027, 1, 1));
        var end = await resolver.ResolveAsync(tenant, employee, new(2027, 12, 31));

        Assert.Equal(EffectiveEmploymentResolutionStatus.Resolved, start.Status);
        Assert.Equal(department, start.Employment!.DepartmentId);
        Assert.Equal(EffectiveEmploymentResolutionStatus.Resolved, end.Status);
    }

    [Fact]
    public async Task Effective_employment_reports_missing_and_overlapping_rows()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenant = Guid.NewGuid(); var employee = Guid.NewGuid();
        using var context = database.CreateContext(new TestTenantContext(tenant));
        AddTenant(context, tenant); context.Employees.Add(NewEmployee(tenant, employee));
        await context.SaveChangesAsync();
        var resolver = new EffectiveEmploymentResolver(context);

        var missing = await resolver.ResolveAsync(tenant, employee, new(2027, 1, 1));
        context.EmployeeEmploymentHistory.AddRange(
            new EmployeeEmploymentHistory { Id = Guid.NewGuid(), TenantId = tenant, EmployeeId = employee, EffectiveFrom = new(2027, 1, 1), EffectiveTo = new(2027, 12, 31) },
            new EmployeeEmploymentHistory { Id = Guid.NewGuid(), TenantId = tenant, EmployeeId = employee, EffectiveFrom = new(2027, 6, 1), EffectiveTo = new(2028, 1, 1) });
        await context.SaveChangesAsync();
        var ambiguous = await resolver.ResolveAsync(tenant, employee, new(2027, 7, 1));

        Assert.Equal(EffectiveEmploymentResolutionStatus.NotFound, missing.Status);
        Assert.Equal(EffectiveEmploymentResolutionStatus.ConfigurationAmbiguity, ambiguous.Status);
    }

    [Fact]
    public async Task Effective_employment_is_tenant_scoped()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantA = Guid.NewGuid(); var tenantB = Guid.NewGuid(); var employee = Guid.NewGuid();
        using var context = database.CreateContext(new TestTenantContext(tenantA));
        AddTenant(context, tenantA); AddTenant(context, tenantB);
        context.Employees.Add(NewEmployee(tenantA, employee));
        context.EmployeeEmploymentHistory.Add(new() { Id = Guid.NewGuid(), TenantId = tenantA, EmployeeId = employee, EffectiveFrom = new(2027, 1, 1) });
        await context.SaveChangesAsync();

        var result = await new EffectiveEmploymentResolver(context).ResolveAsync(tenantB, employee, new(2027, 1, 1));

        Assert.Equal(EffectiveEmploymentResolutionStatus.InvalidTenant, result.Status);
    }

    private static Employee NewEmployee(Guid tenant, Guid id) => new()
    {
        Id = id, TenantId = tenant, FirstName = "Test", LastName = "Employee",
        Email = $"{id}@test.local", DateOfJoining = new(2026, 1, 1), Gender = Gender.Unspecified
    };

    private static void AddTenant(HRMS.Infrastructure.Persistence.HrmsDbContext context, Guid id) =>
        context.Tenants.Add(new Tenant { Id = id, TenantCode = id.ToString("N")[..8], Host = $"{id}.local", ShardKey = id.ToString("N"), TenantName = "Test" });
}
