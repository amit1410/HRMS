using HRMS.Application.DTOs.Roles;
using HRMS.Application.Services;
using HRMS.Domain.Authorization;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit.Sdk;

namespace HRMS.Tests;

/// <summary>
/// Relational race coverage using the existing disposable MySQL lifecycle fixture. This is deliberately
/// not an SQLite/InMemory test: overlap protection must be verified with the provider transaction semantics.
/// </summary>
public sealed class RoleAssignmentConcurrencyTests
{
    [Fact]
    public async Task Concurrent_overlapping_assignments_leave_one_assignment_and_one_event()
    {
        var connection = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
            throw SkipException.ForSkip("Role assignment concurrency test not executed: HRMS_MYSQL_TEST_CONNECTION is absent.");

        var fixture = new MySqlLeaveLifecycleIntegrationTests.Fixture(connection);
        var roleId = SeedData.RoleId(RoleNames.HRBP);
        var scopeId = Guid.NewGuid();
        try
        {
            await fixture.SeedAsync();
            await EnsureRoleAsync(fixture, roleId);
            await EnsureScopeAsync(fixture, scopeId);

            var scope = new RoleAssignmentScopeDto(RoleScopeType.HoldingCompany, scopeId);
            var first = AssignAsync(fixture, new(2027, 1, 1), new(2027, 6, 30), roleId, scope);
            var second = AssignAsync(fixture, new(2027, 3, 1), new(2027, 9, 30), roleId, scope);
            var results = await Task.WhenAll(first, second);

            Assert.Single(results, x => x.Succeeded);
            Assert.Single(results, x => x.Status == HRMS.Application.Common.ResultStatus.Conflict);

            await using var verify = fixture.CreateContext(new TestTenantContext(fixture.TenantId));
            var assignments = await verify.UserRoles.IgnoreQueryFilters()
                .Where(x => x.TenantId == fixture.TenantId && x.UserId == fixture.ManagerUserId && x.RoleId == roleId)
                .ToListAsync();
            Assert.Single(assignments);
            Assert.Single(await verify.UserRoleAssignmentEvents.IgnoreQueryFilters()
                .Where(x => x.TenantId == fixture.TenantId && x.AssignmentId == assignments[0].Id && x.EventType == HRMS.Domain.Enums.UserRoleAssignmentEventType.Assigned)
                .ToListAsync());
            Assert.Single(await verify.UserRoleAssignmentScopes.IgnoreQueryFilters()
                .Where(x => x.TenantId == fixture.TenantId && x.UserRoleAssignmentId == assignments[0].Id)
                .ToListAsync());
        }
        finally
        {
            await CleanupAsync(fixture, roleId, scopeId);
            await fixture.CleanupAsync();
        }
    }

    [Fact]
    public async Task Concurrent_non_overlapping_assignments_both_succeed_and_persist_events()
    {
        var connection = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
            throw SkipException.ForSkip("Role assignment concurrency test not executed: HRMS_MYSQL_TEST_CONNECTION is absent.");

        var fixture = new MySqlLeaveLifecycleIntegrationTests.Fixture(connection);
        var roleId = SeedData.RoleId(RoleNames.HRBP);
        var scopeId = Guid.NewGuid();
        try
        {
            await fixture.SeedAsync();
            await EnsureRoleAsync(fixture, roleId);
            await EnsureScopeAsync(fixture, scopeId);

            var scope = new RoleAssignmentScopeDto(RoleScopeType.HoldingCompany, scopeId);
            var first = AssignAsync(fixture, new(2027, 1, 1), new(2027, 1, 31), roleId, scope);
            var second = AssignAsync(fixture, new(2027, 2, 1), new(2027, 2, 28), roleId, scope);
            var results = await Task.WhenAll(first, second);

            Assert.All(results, x => Assert.True(x.Succeeded));
            await using var verify = fixture.CreateContext(new TestTenantContext(fixture.TenantId));
            var assignments = await verify.UserRoles.IgnoreQueryFilters()
                .Where(x => x.TenantId == fixture.TenantId && x.UserId == fixture.ManagerUserId && x.RoleId == roleId)
                .ToListAsync();
            Assert.Equal(2, assignments.Count);
            Assert.Equal(2, await verify.UserRoleAssignmentEvents.IgnoreQueryFilters()
                .CountAsync(x => x.TenantId == fixture.TenantId && assignments.Select(a => a.Id).Contains(x.AssignmentId) && x.EventType == HRMS.Domain.Enums.UserRoleAssignmentEventType.Assigned));
            Assert.Equal(2, await verify.UserRoleAssignmentScopes.IgnoreQueryFilters()
                .CountAsync(x => x.TenantId == fixture.TenantId && assignments.Select(a => a.Id).Contains(x.UserRoleAssignmentId)));
        }
        finally
        {
            await CleanupAsync(fixture, roleId, scopeId);
            await fixture.CleanupAsync();
        }
    }

    private static async Task<HRMS.Application.Common.Result<HRMS.Application.DTOs.Roles.RoleAssignmentDto>> AssignAsync(
        MySqlLeaveLifecycleIntegrationTests.Fixture fixture, DateOnly from, DateOnly to, int roleId, RoleAssignmentScopeDto scope)
    {
        await using var db = fixture.CreateContext(new TestTenantContext(fixture.TenantId, fixture.ManagerUserId));
        return await new RoleAssignmentService(db, new TestTenantContext(fixture.TenantId, fixture.ManagerUserId), TimeProvider.System, new MySqlTransientErrorClassifier())
            .AssignAsync(fixture.ManagerUserId, new RoleAssignmentRequest(roleId, from, to, "concurrency test", [scope]));
    }

    private static async Task EnsureRoleAsync(MySqlLeaveLifecycleIntegrationTests.Fixture fixture, int roleId)
    {
        await using var db = fixture.CreateContext(new TestTenantContext());
        if (!await db.Roles.AnyAsync(x => x.Id == roleId))
            db.Roles.Add(new Role { Id = roleId, Name = RoleNames.HRBP, Description = "Concurrency test role" });
        await db.SaveChangesAsync();
    }

    private static async Task EnsureScopeAsync(MySqlLeaveLifecycleIntegrationTests.Fixture fixture, Guid scopeId)
    {
        await using var db = fixture.CreateContext(new TestTenantContext());
        db.HoldingCompanies.Add(new HoldingCompany { Id = scopeId, TenantId = fixture.TenantId, Code = $"HC{scopeId:N}"[..8], Name = "Concurrency scope" });
        await db.SaveChangesAsync();
    }

    private static async Task CleanupAsync(MySqlLeaveLifecycleIntegrationTests.Fixture fixture, int roleId, Guid scopeId)
    {
        await using var db = fixture.CreateContext(new TestTenantContext());
        var ids = await db.UserRoles.IgnoreQueryFilters()
            .Where(x => x.TenantId == fixture.TenantId && x.UserId == fixture.ManagerUserId && x.RoleId == roleId)
            .Select(x => x.Id).ToListAsync();
        if (ids.Count > 0)
        {
            await db.UserRoleAssignmentScopes.IgnoreQueryFilters().Where(x => ids.Contains(x.UserRoleAssignmentId)).ExecuteDeleteAsync();
            await db.UserRoleAssignmentEvents.IgnoreQueryFilters().Where(x => ids.Contains(x.AssignmentId)).ExecuteDeleteAsync();
            await db.UserRoles.IgnoreQueryFilters().Where(x => ids.Contains(x.Id)).ExecuteDeleteAsync();
        }
        await db.HoldingCompanies.IgnoreQueryFilters().Where(x => x.TenantId == fixture.TenantId && x.Id == scopeId).ExecuteDeleteAsync();
    }
}
