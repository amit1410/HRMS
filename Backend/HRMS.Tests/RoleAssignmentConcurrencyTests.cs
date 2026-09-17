using HRMS.Application.DTOs.Roles;
using HRMS.Application.Common;
using HRMS.Application.Services;
using HRMS.API.Controllers;
using HRMS.Domain.Authorization;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Infrastructure.Security;
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
    public async Task MySql_assignment_pagination_supports_page_two_and_total_count()
    {
        var connection = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
            throw SkipException.ForSkip("Role assignment pagination test not executed: HRMS_MYSQL_TEST_CONNECTION is absent.");

        var fixture = new MySqlLeaveLifecycleIntegrationTests.Fixture(connection);
        var roleId = Random.Shared.Next(900_000, 999_000);
        var assignmentUserId = fixture.EmployeeUserId;
        try
        {
            await fixture.SeedAsync();
            await using (var db = fixture.CreateContext(new TestTenantContext()))
            {
                if (!await db.Roles.AnyAsync(x => x.Id == roleId))
                    db.Roles.Add(new Role { Id = roleId, Name = $"Pagination{fixture.TenantId:N}"[..16] });
                await db.SaveChangesAsync();
            }

            await using (var db = fixture.CreateContext(new TestTenantContext(fixture.TenantId, fixture.ManagerUserId)))
            {
                var service = new RoleAssignmentService(db, new TestTenantContext(fixture.TenantId, fixture.ManagerUserId), TimeProvider.System);
                var first = await service.AssignAsync(assignmentUserId, new(roleId, new(2080, 1, 1), new(2080, 1, 31), "pagination", null));
                var second = await service.AssignAsync(assignmentUserId, new(roleId, new(2080, 2, 1), new(2080, 2, 29), "pagination", null));
                Assert.True(first.Succeeded, first.Message);
                Assert.True(second.Succeeded, second.Message);

                var pageOne = await service.GetAssignmentsAsync(new RoleAssignmentQuery { Page = 1, PageSize = 1, RoleId = roleId });
                var pageTwo = await service.GetAssignmentsAsync(new RoleAssignmentQuery { Page = 2, PageSize = 1, RoleId = roleId });
                Assert.True(pageOne.Succeeded, pageOne.Message);
                Assert.True(pageTwo.Succeeded, pageTwo.Message);
                Assert.Equal(2, pageOne.Value!.TotalCount);
                Assert.Single(pageOne.Value.Items);
                Assert.Single(pageTwo.Value!.Items);
                Assert.NotEqual(pageOne.Value.Items[0].AssignmentId, pageTwo.Value.Items[0].AssignmentId);

                var scheduled = await service.GetAssignmentsAsync(new RoleAssignmentQuery { Page = 1, PageSize = 1, RoleId = roleId, Status = "Scheduled", Source = RoleAssignmentSource.Manual });
                Assert.True(scheduled.Succeeded, scheduled.Message);
                Assert.Equal(2, scheduled.Value!.TotalCount);
            }
        }
        finally
        {
            await CleanupAsync(fixture, roleId, Guid.Empty, assignmentUserId);
            await fixture.CleanupAsync();
        }
    }

    [Fact]
    public async Task MySql_assignment_hydration_handles_twenty_database_derived_ids()
    {
        var connection = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
            throw SkipException.ForSkip("MySQL multi-GUID hydration test not executed: HRMS_MYSQL_TEST_CONNECTION is absent.");

        var fixture = new MySqlLeaveLifecycleIntegrationTests.Fixture(connection);
        var roleIds = Enumerable.Range(0, 20).Select(_ => Random.Shared.Next(1_100_000, 1_900_000)).ToArray();
        var assignmentIds = Enumerable.Range(0, 20).Select(_ => Guid.NewGuid()).ToArray();
        try
        {
            await fixture.SeedAsync();
            await using (var db = fixture.CreateContext(new TestTenantContext()))
            {
                var roleNamePrefix = fixture.TenantId.ToString("N")[..10];
                db.Roles.AddRange(roleIds.Select((roleId, index) => new Role { Id = roleId, Name = $"MultiGuid{roleNamePrefix}{index:D2}" }));
                db.UserRoles.AddRange(assignmentIds.Select((id, index) => new UserRole
                {
                    Id = id,
                    TenantId = fixture.TenantId,
                    UserId = fixture.EmployeeUserId,
                    RoleId = roleIds[index],
                    EffectiveFrom = new DateOnly(2090, 1, 1),
                    EffectiveTo = new DateOnly(2090, 12, 31),
                    AssignmentSource = RoleAssignmentSource.Manual,
                    AssignedByUserId = fixture.ManagerUserId,
                    AssignmentReason = "multi-guid provider regression",
                    CreatedAtUtc = DateTime.UtcNow
                }));
                await db.SaveChangesAsync();
            }

            await using var context = fixture.CreateContext(new TestTenantContext(fixture.TenantId, fixture.ManagerUserId));
            var service = new RoleAssignmentService(context, new TestTenantContext(fixture.TenantId, fixture.ManagerUserId), TimeProvider.System);
            var result = await service.GetAssignmentsAsync(new RoleAssignmentQuery { Source = RoleAssignmentSource.Manual, PageSize = 20 });
            Assert.True(result.Succeeded, result.Message);
            Assert.True(result.Value!.TotalCount >= 20);
            Assert.Equal(20, result.Value.Items.Count);
        }
        finally
        {
            await using var cleanup = fixture.CreateContext(new TestTenantContext());
            foreach (var assignmentId in assignmentIds)
            {
                var assignment = await cleanup.UserRoles.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Id == assignmentId);
                if (assignment is not null) cleanup.UserRoles.Remove(assignment);
            }
            await cleanup.SaveChangesAsync();
            foreach (var roleId in roleIds)
            {
                var role = await cleanup.Roles.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Id == roleId);
                if (role is not null) cleanup.Roles.Remove(role);
            }
            await cleanup.SaveChangesAsync();
            await fixture.CleanupAsync();
        }
    }

    [Fact]
    public async Task MySql_candidate_pagination_supports_page_two_and_total_count()
    {
        var connection = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
            throw SkipException.ForSkip("Role management candidate pagination test not executed: HRMS_MYSQL_TEST_CONNECTION is absent.");

        var fixture = new MySqlLeaveLifecycleIntegrationTests.Fixture(connection);
        var candidateIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        try
        {
            await fixture.SeedAsync();
            await using (var db = fixture.CreateContext(new TestTenantContext()))
            {
                var hasher = new IdentityPasswordHasher();
                db.Users.AddRange(candidateIds.Select((id, index) => new User
                {
                    Id = id,
                    TenantId = fixture.TenantId,
                    Email = $"role-page-{index}-{id:N}@test.invalid",
                    PasswordHash = hasher.Hash("Passw0rd!123"),
                    FirstName = "Role",
                    LastName = $"Candidate{index}",
                    IsActive = true
                }));
                await db.SaveChangesAsync();
            }

            await using var context = fixture.CreateContext(new TestTenantContext(fixture.TenantId, fixture.ManagerUserId));
            var service = new RoleAssignmentService(context, new TestTenantContext(fixture.TenantId, fixture.ManagerUserId), TimeProvider.System);
            var pageOne = await service.GetCandidatesAsync(new RoleAssignmentQuery { Page = 1, PageSize = 1, Search = "Role" });
            var pageTwo = await service.GetCandidatesAsync(new RoleAssignmentQuery { Page = 2, PageSize = 1, Search = "Role" });
            Assert.True(pageOne.Succeeded, pageOne.Message);
            Assert.True(pageTwo.Succeeded, pageTwo.Message);
            Assert.Equal(2, pageOne.Value!.TotalCount);
            Assert.Single(pageOne.Value.Items);
            Assert.Single(pageTwo.Value!.Items);
            Assert.NotEqual(pageOne.Value.Items[0].UserId, pageTwo.Value.Items[0].UserId);
        }
        finally
        {
            await using var cleanup = fixture.CreateContext(new TestTenantContext());
            foreach (var candidateId in candidateIds)
            {
                var candidate = await cleanup.Users.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Id == candidateId);
                if (candidate is not null) cleanup.Users.Remove(candidate);
            }
            await cleanup.SaveChangesAsync();
            await fixture.CleanupAsync();
        }
    }

    [Fact]
    public async Task MySql_role_management_filter_matrix_executes_without_provider_failure()
    {
        var connection = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
            throw SkipException.ForSkip("Role management filter matrix test not executed: HRMS_MYSQL_TEST_CONNECTION is absent.");

        var fixture = new MySqlLeaveLifecycleIntegrationTests.Fixture(connection);
        try
        {
            await fixture.SeedAsync();
            await using var db = fixture.CreateContext(new TestTenantContext(fixture.TenantId, fixture.ManagerUserId));
            var service = new RoleAssignmentService(db, new TestTenantContext(fixture.TenantId, fixture.ManagerUserId), TimeProvider.System);

            foreach (var query in new[]
            {
                new RoleAssignmentQuery(),
                new RoleAssignmentQuery { Status = "Active" },
                new RoleAssignmentQuery { Source = RoleAssignmentSource.System },
                new RoleAssignmentQuery { Source = RoleAssignmentSource.Manual },
                new RoleAssignmentQuery { Search = "Leave" },
                new RoleAssignmentQuery { Status = "Active", Source = RoleAssignmentSource.Manual },
                new RoleAssignmentQuery { Search = "Leave", Source = RoleAssignmentSource.Manual }
            })
            {
                var result = await service.GetAssignmentsAsync(query);
                Assert.True(result.Succeeded, result.Message);
            }

            Assert.True((await service.GetCandidatesAsync(new PagedQueryModel())).Succeeded);
            Assert.True((await service.GetCandidatesAsync(new PagedQueryModel { Search = "Leave" })).Succeeded);
        }
        finally
        {
            await fixture.CleanupAsync();
        }
    }

    [Fact]
    public async Task MySql_assignment_listing_handles_legacy_assignment_without_optional_relationships()
    {
        var connection = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
            throw SkipException.ForSkip("Legacy role assignment listing test not executed: HRMS_MYSQL_TEST_CONNECTION is absent.");

        var fixture = new MySqlLeaveLifecycleIntegrationTests.Fixture(connection);
        var roleId = Random.Shared.Next(900_000, 999_000);
        var userId = Guid.NewGuid();
        var assignmentId = Guid.NewGuid();
        try
        {
            await fixture.SeedAsync();
            await using (var db = fixture.CreateContext(new TestTenantContext()))
            {
                db.Roles.Add(new Role { Id = roleId, Name = $"Legacy{fixture.TenantId:N}"[..12] });
                db.Users.Add(new User
                {
                    Id = userId,
                    TenantId = fixture.TenantId,
                    Email = $"legacy-{userId:N}@test.invalid",
                    PasswordHash = new IdentityPasswordHasher().Hash("Passw0rd!123"),
                    FirstName = "Legacy",
                    LastName = "Assignment",
                    IsActive = true
                });
                db.UserRoles.Add(new UserRole
                {
                    Id = assignmentId,
                    TenantId = fixture.TenantId,
                    UserId = userId,
                    RoleId = roleId,
                    EffectiveFrom = new(2026, 9, 15),
                    AssignmentSource = RoleAssignmentSource.System,
                    AssignedByUserId = null,
                    AssignmentReason = null
                });
                await db.SaveChangesAsync();
            }

            await using var context = fixture.CreateContext(new TestTenantContext(fixture.TenantId, fixture.ManagerUserId));
            var service = new RoleAssignmentService(context, new TestTenantContext(fixture.TenantId, fixture.ManagerUserId), TimeProvider.System);
            var result = await service.GetAssignmentsAsync(new RoleAssignmentQuery { Page = 1, PageSize = 10, RoleId = roleId });
            Assert.True(result.Succeeded, result.Message);
            var item = Assert.Single(result.Value!.Items);
            Assert.Equal(assignmentId, item.AssignmentId);
            Assert.Null(item.EmployeeId);
            Assert.Equal("Tenant-wide", item.ScopeSummary);
            Assert.False(item.IsRevoked);
            Assert.Null(item.RevokedEffectiveDate);
        }
        finally
        {
            await using var cleanup = fixture.CreateContext(new TestTenantContext());
            await cleanup.UserRoles.IgnoreQueryFilters().Where(x => x.Id == assignmentId).ExecuteDeleteAsync();
            await cleanup.Users.IgnoreQueryFilters().Where(x => x.Id == userId).ExecuteDeleteAsync();
            await cleanup.Roles.IgnoreQueryFilters().Where(x => x.Id == roleId).ExecuteDeleteAsync();
            await fixture.CleanupAsync();
        }
    }

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

    private static Task CleanupAsync(MySqlLeaveLifecycleIntegrationTests.Fixture fixture, int roleId, Guid scopeId) =>
        CleanupAsync(fixture, roleId, scopeId, fixture.ManagerUserId);

    private static async Task CleanupAsync(MySqlLeaveLifecycleIntegrationTests.Fixture fixture, int roleId, Guid scopeId, Guid userId)
    {
        await using var db = fixture.CreateContext(new TestTenantContext());
        var ids = await db.UserRoles.IgnoreQueryFilters()
            .Where(x => x.TenantId == fixture.TenantId && x.UserId == userId && x.RoleId == roleId)
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
