using HRMS.Application.Services;
using HRMS.Application.Common;
using HRMS.Domain.Entities;
using HRMS.Domain.Entities.Separation;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using SeparationReasonEntity = HRMS.Domain.Entities.Separation.SeparationReason;

namespace HRMS.Tests;

public sealed class SeparationFoundationTests
{
    [Fact]
    public async Task Employee_can_create_submit_and_withdraw_resignation_without_payroll_side_effects()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid(); var employeeId = Guid.NewGuid(); var userId = Guid.NewGuid();
        await SeedTenantAsync(database, tenantId);
        await SeedEmployeeAsync(database, tenantId, employeeId, userId, linked: true);
        var tenant = new TestTenantContext(tenantId, userId);
        await using var context = database.CreateContext(tenant);
        var reason = await AddReasonAsync(context, tenantId, employeeAllowed: true, employerAllowed: false);
        var beforeLeaving = await context.Employees.Where(x => x.Id == employeeId).Select(x => x.DateOfLeaving).SingleAsync();
        var service = CreateService(context, tenant);

        var created = await service.CreateSelfAsync(new(reason, new(2026, 9, 23), new(2026, 10, 23), "Moving on"));
        Assert.True(created.Succeeded);
        Assert.Equal(EmployeeSeparationStatus.Draft, created.Value!.Status);
        Assert.True((await service.SubmitAsync(created.Value.Id)).Succeeded);
        Assert.True((await service.WithdrawAsync(created.Value.Id)).Succeeded);

        var stored = await context.EmployeeSeparations.SingleAsync(x => x.Id == created.Value.Id);
        Assert.Equal(EmployeeSeparationStatus.Withdrawn, stored.Status);
        Assert.Equal(beforeLeaving, await context.Employees.Where(x => x.Id == employeeId).Select(x => x.DateOfLeaving).SingleAsync());
        Assert.Equal(3, await context.EmployeeSeparationEvents.CountAsync(x => x.EmployeeSeparationId == stored.Id));
    }

    [Fact]
    public async Task Self_service_requires_authoritative_account_employee_link()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid(); var employeeId = Guid.NewGuid(); var userId = Guid.NewGuid();
        await SeedTenantAsync(database, tenantId); await SeedEmployeeAsync(database, tenantId, employeeId, userId, linked: false);
        var tenant = new TestTenantContext(tenantId, userId);
        await using var context = database.CreateContext(tenant);
        var reason = await AddReasonAsync(context, tenantId, true, false);
        var result = await CreateService(context, tenant).CreateSelfAsync(new(reason, new(2026, 9, 23), new(2026, 10, 23), null));
        Assert.False(result.Succeeded);
        Assert.Equal(ResultStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task Employee_cannot_use_employer_only_reason_or_create_duplicate_active_case()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid(); var employeeId = Guid.NewGuid(); var userId = Guid.NewGuid();
        await SeedTenantAsync(database, tenantId); await SeedEmployeeAsync(database, tenantId, employeeId, userId, linked: true);
        var tenant = new TestTenantContext(tenantId, userId);
        await using var context = database.CreateContext(tenant);
        var employerOnly = await AddReasonAsync(context, tenantId, false, true);
        var employeeOnly = await AddReasonAsync(context, tenantId, true, false, "RESIGN");
        var service = CreateService(context, tenant);
        var invalid = await service.CreateSelfAsync(new(employerOnly, new(2026, 9, 23), new(2026, 10, 23), null));
        Assert.False(invalid.Succeeded);
        var first = await service.CreateSelfAsync(new(employeeOnly, new(2026, 9, 23), new(2026, 10, 23), null));
        var duplicate = await service.CreateSelfAsync(new(employeeOnly, new(2026, 9, 23), new(2026, 11, 23), null));
        Assert.True(first.Succeeded); Assert.False(duplicate.Succeeded); Assert.Equal(ResultStatus.Conflict, duplicate.Status);
    }

    [Fact]
    public async Task Hr_can_initiate_employer_separation_but_tenant_foreign_employee_is_denied()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid(); var otherTenantId = Guid.NewGuid(); var employeeId = Guid.NewGuid(); var actor = Guid.NewGuid();
        await SeedTenantAsync(database, tenantId); await SeedTenantAsync(database, otherTenantId);
        await SeedEmployeeAsync(database, tenantId, employeeId, Guid.NewGuid(), linked: false);
        await SeedEmployeeAsync(database, otherTenantId, Guid.NewGuid(), Guid.NewGuid(), linked: false);
        var tenant = new TestTenantContext(tenantId, actor);
        await using var context = database.CreateContext(tenant);
        var reason = await AddReasonAsync(context, tenantId, false, true);
        var service = CreateService(context, tenant);
        var created = await service.CreateForEmployeeAsync(employeeId, new(reason, new(2026, 9, 23), new(2026, 10, 23), "Role redundancy"));
        Assert.True(created.Succeeded); Assert.Equal(SeparationType.EmployerInitiated, created.Value!.SeparationType);
        var foreign = await service.CreateForEmployeeAsync(Guid.NewGuid(), new(reason, new(2026, 9, 23), new(2026, 10, 23), null));
        Assert.False(foreign.Succeeded);
    }

    [Fact]
    public async Task Tenant_isolation_and_history_are_enforced()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantA = Guid.NewGuid(); var tenantB = Guid.NewGuid(); var employee = Guid.NewGuid(); var user = Guid.NewGuid();
        await SeedTenantAsync(database, tenantA); await SeedTenantAsync(database, tenantB); await SeedEmployeeAsync(database, tenantA, employee, user, linked: true);
        var contextA = new TestTenantContext(tenantA, user); await using var dbA = database.CreateContext(contextA);
        var reason = await AddReasonAsync(dbA, tenantA, true, false);
        var service = CreateService(dbA, contextA); var created = await service.CreateSelfAsync(new(reason, new(2026, 9, 23), new(2026, 10, 23), null));
        Assert.True(created.Succeeded); Assert.True((await service.SubmitAsync(created.Value!.Id)).Succeeded);
        var contextB = new TestTenantContext(tenantB, user); await using var dbB = database.CreateContext(contextB);
        var hidden = await new SeparationService(dbB, contextB, new EmployeeIdentityResolver(dbB, contextB), new EmployeeManagerResolver(dbB, contextB), TimeProvider.System).GetAsync(created.Value.Id);
        Assert.False(hidden.Succeeded);
        Assert.Equal(2, await dbA.EmployeeSeparationEvents.CountAsync(x => x.EmployeeSeparationId == created.Value.Id));
    }

    [Fact]
    public async Task Concurrent_self_resignations_leave_one_active_case()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid(); var employeeId = Guid.NewGuid(); var userId = Guid.NewGuid();
        await SeedTenantAsync(database, tenantId); await SeedEmployeeAsync(database, tenantId, employeeId, userId, linked: true);
        await using var seed = database.CreateContext(new TestTenantContext(tenantId, userId));
        var reasonId = await AddReasonAsync(seed, tenantId, true, false);
        await using var dbA = database.CreateContext(new TestTenantContext(tenantId, userId));
        await using var dbB = database.CreateContext(new TestTenantContext(tenantId, userId));
        var tenantA = new TestTenantContext(tenantId, userId); var tenantB = new TestTenantContext(tenantId, userId);
        var serviceA = CreateService(dbA, tenantA); var serviceB = CreateService(dbB, tenantB);
        var results = await Task.WhenAll(
            serviceA.CreateSelfAsync(new(reasonId, new(2026, 9, 23), new(2026, 10, 23), "A")),
            serviceB.CreateSelfAsync(new(reasonId, new(2026, 9, 23), new(2026, 10, 23), "B")));
        Assert.Single(results, result => result.Succeeded);
        await using var verify = database.CreateContext(new TestTenantContext(tenantId, userId));
        Assert.Equal(1, await verify.EmployeeSeparations.CountAsync(x => x.EmployeeId == employeeId && x.Status == EmployeeSeparationStatus.Draft));
    }

    private static SeparationService CreateService(HRMS.Infrastructure.Persistence.HrmsDbContext db, TestTenantContext tenant) => new(db, tenant, new EmployeeIdentityResolver(db, tenant), new EmployeeManagerResolver(db, tenant), TimeProvider.System);

    private static async Task<Guid> AddReasonAsync(HRMS.Infrastructure.Persistence.HrmsDbContext db, Guid tenantId, bool employeeAllowed, bool employerAllowed, string code = "VOLUNTARY")
    {
        var reason = new SeparationReasonEntity { Id = Guid.NewGuid(), TenantId = tenantId, Code = code + Guid.NewGuid().ToString("N")[..6], Name = "Test reason", Category = SeparationReasonCategory.Resignation, EmployeeInitiatedAllowed = employeeAllowed, EmployerInitiatedAllowed = employerAllowed, IsActive = true, EffectiveFrom = new(2020, 1, 1) };
        db.SeparationReasons.Add(reason); await db.SaveChangesAsync(); return reason.Id;
    }

    private static async Task SeedTenantAsync(SqliteInMemoryDatabase database, Guid tenantId)
    {
        await using var db = database.CreateContext(new TestTenantContext());
        db.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"T{tenantId:N}"[..20], Host = $"{tenantId:N}.test", ShardKey = tenantId.ToString("N") }); await db.SaveChangesAsync();
    }

    private static async Task SeedEmployeeAsync(SqliteInMemoryDatabase database, Guid tenantId, Guid employeeId, Guid userId, bool linked)
    {
        await using var db = database.CreateContext(new TestTenantContext(tenantId, userId));
        db.Employees.Add(new Employee { Id = employeeId, TenantId = tenantId, EmployeeCode = $"E-{employeeId:N}"[..12], FirstName = "Test", LastName = "Employee", Email = $"{employeeId:N}@test.local", DateOfJoining = new(2025, 1, 1) });
        if (linked)
        {
            db.Users.Add(new User { Id = userId, TenantId = tenantId, Email = $"{userId:N}@test.local", PasswordHash = "test-hash", FirstName = "Test", LastName = "User", IsActive = true });
            var linkId = Guid.NewGuid();
            db.AccountEmployeeLinkEvents.Add(new AccountEmployeeLinkEvent { Id = linkId, TenantId = tenantId, SubjectUserId = userId, ActorUserId = userId, Sequence = 1, Operation = "Link", NewLinkId = linkId, AfterEmployeeId = employeeId, OccurredAtUtc = DateTime.UtcNow, Reason = "test", CorrelationId = linkId.ToString("N") });
            db.AccountEmployeeCurrentLinks.Add(new AccountEmployeeCurrentLink { LinkId = linkId, TenantId = tenantId, UserId = userId, EmployeeId = employeeId });
        }
        await db.SaveChangesAsync();
    }
}
