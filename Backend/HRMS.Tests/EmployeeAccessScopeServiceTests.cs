using HRMS.Application.Services;
using HRMS.Domain.Authorization;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

/// <summary>
/// <see cref="EmployeeAccessScopeService.BuildPredicateAsync"/> used to deny every employee to a
/// TenantAdmin whose role assignment was recorded with <see cref="RoleAssignmentSource.System"/> and
/// no explicit <see cref="UserRoleAssignmentScope"/> rows (exactly how tenant provisioning and the QA
/// automation seed both assign TenantAdmin) — the "unscoped assignment is tenant-wide" shortcut only
/// looked at assignment provenance (<c>AssignmentSource</c>), not at what the role can actually do.
/// These tests pin the corrected behaviour: a genuinely broad role gets tenant-wide access regardless
/// of how it was assigned, a role that is only ever scoped by org hierarchy (Manager) stays scoped to
/// self and direct reports, an explicitly dimension-scoped role stays scoped to its dimension, and none
/// of this crosses a tenant boundary.
/// </summary>
public sealed class EmployeeAccessScopeServiceTests
{
    private static readonly Guid Demo01 = SeedData.TenantIds.Demo01;
    private static readonly Guid Demo02 = SeedData.TenantIds.Demo02;

    [Fact]
    public async Task TenantAdmin_with_system_seeded_unscoped_role_sees_every_tenant_employee()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        var userId = await SeedUserWithRoleAsync(harness, Demo01, RoleNames.TenantAdmin, AssignmentSourceForTest: RoleAssignmentSource.System);

        var tenantContext = new TestTenantContext(Demo01, userId);
        await using var db = harness.Database.CreateContext(tenantContext);
        var accessScope = new EmployeeAccessScopeService(db, tenantContext);

        var predicate = await accessScope.BuildPredicateAsync(new DateOnly(2026, 3, 4));
        var visible = await db.Employees.Where(predicate).CountAsync();

        // Matches EmployeeServiceTests.Get_returns_only_the_callers_own_employees, which reads the
        // same seed data with no scope predicate applied at all (6 employees in Demo01).
        Assert.Equal(6, visible);
    }

    [Fact]
    public async Task Manager_role_with_no_explicit_scope_stays_limited_to_self_and_direct_reports()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        var managerEmployeeId = OrganizationTestHarness.EmployeeId(Demo01, "EMP-002");
        var userId = await SeedUserWithRoleAsync(
            harness, Demo01, RoleNames.Manager, RoleAssignmentSource.System, linkedEmployeeId: managerEmployeeId);

        var tenantContext = new TestTenantContext(Demo01, userId);
        await using var db = harness.Database.CreateContext(tenantContext);
        var accessScope = new EmployeeAccessScopeService(db, tenantContext);

        var predicate = await accessScope.BuildPredicateAsync(new DateOnly(2026, 3, 4));
        var visibleCodes = await db.Employees.Where(predicate).Select(e => e.EmployeeCode).ToListAsync();

        // EMP-002 is the reporting manager for EMP-003 and EMP-004 (per
        // EmployeeServiceTests.Get_filters_by_department_designation_status_and_manager). A Manager's
        // reach comes from the org hierarchy, not a dimensional scope row, so it must stay narrow even
        // though the assignment itself is unscoped — this must NOT become tenant-wide.
        Assert.Contains("EMP-002", visibleCodes);
        Assert.Contains("EMP-003", visibleCodes);
        Assert.Contains("EMP-004", visibleCodes);
        Assert.DoesNotContain("EMP-001", visibleCodes);
        Assert.True(visibleCodes.Count < 6, "A Manager's unscoped assignment must not grant tenant-wide access.");
    }

    [Fact]
    public async Task Custom_role_with_an_explicit_department_scope_stays_limited_to_that_department()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        var engineering = OrganizationTestHarness.DepartmentId(Demo01, "ENG");
        var finance = OrganizationTestHarness.DepartmentId(Demo01, "FIN");

        Guid userId;
        int roleId;
        Guid inScopeEmployeeId, outOfScopeEmployeeId;
        await using (var setup = harness.Database.CreateContext(new TestTenantContext(Demo01)))
        {
            userId = Guid.NewGuid();
            setup.Users.Add(new User { Id = userId, TenantId = Demo01, Email = $"scoped-hr-{userId:N}@test.invalid", FirstName = "Scoped", LastName = "HR", IsActive = true });

            roleId = Random.Shared.Next(100_000, 900_000);
            setup.Roles.Add(new Role { Id = roleId, Name = $"Scoped HR {roleId}" });
            var permission = await setup.Permissions.SingleAsync(x => x.Name == Permissions.Employee.View);
            setup.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionId = permission.Id });

            // System-sourced on purpose: this is the exact shape the defect report was about (a
            // System-seeded assignment) — it must remain department-scoped because it carries an
            // explicit UserRoleAssignmentScope row, independent of AssignmentSource.
            var assignment = new UserRole { Id = Guid.NewGuid(), TenantId = Demo01, UserId = userId, RoleId = roleId, EffectiveFrom = new DateOnly(2026, 1, 1), AssignmentSource = RoleAssignmentSource.System };
            setup.UserRoles.Add(assignment);
            setup.UserRoleAssignmentScopes.Add(new UserRoleAssignmentScope { Id = Guid.NewGuid(), TenantId = Demo01, UserRoleAssignmentId = assignment.Id, ScopeType = RoleScopeType.Department, ScopeEntityId = engineering });

            // The seeded demo employees carry their department only on the legacy Employee.DepartmentId
            // field, with no EmployeeEmploymentHistory rows — but the dimensional predicate this test
            // exercises reads EmploymentHistory, not the legacy field. So this test provisions its own
            // pair of employees, one inside the scoped department and one outside it, each with the
            // EmploymentHistory row the predicate actually needs.
            inScopeEmployeeId = AddEmployeeWithHistory(setup, "In Scope", engineering);
            outOfScopeEmployeeId = AddEmployeeWithHistory(setup, "Out Of Scope", finance);
            await setup.SaveChangesAsync();
        }

        var tenantContext = new TestTenantContext(Demo01, userId);
        await using var db = harness.Database.CreateContext(tenantContext);
        var accessScope = new EmployeeAccessScopeService(db, tenantContext);

        var predicate = await accessScope.BuildPredicateAsync(new DateOnly(2026, 3, 4));
        var visibleIds = await db.Employees.Where(predicate).Select(e => e.Id).ToListAsync();

        // An explicitly scoped assignment must stay scoped even though the role holds the same
        // Employee.View permission a broad role would use to qualify for tenant-wide access when
        // unscoped — the department dimension, not the permission, is what must gate this.
        Assert.Contains(inScopeEmployeeId, visibleIds);
        Assert.DoesNotContain(outOfScopeEmployeeId, visibleIds);
    }

    private static Guid AddEmployeeWithHistory(HRMS.Infrastructure.Persistence.HrmsDbContext db, string firstName, Guid departmentId)
    {
        var id = Guid.NewGuid();
        db.Employees.Add(new Employee { Id = id, TenantId = Demo01, EmployeeCode = $"S{id:N}"[..10], FirstName = firstName, LastName = "Employee", Email = $"{id:N}@test.invalid", DateOfJoining = new DateOnly(2020, 1, 1), Status = EmployeeStatus.Active });
        db.EmployeeEmploymentHistory.Add(new EmployeeEmploymentHistory { Id = Guid.NewGuid(), TenantId = Demo01, EmployeeId = id, EffectiveFrom = new DateOnly(2026, 1, 1), DepartmentId = departmentId, EmploymentStatus = EmployeeStatus.Active });
        return id;
    }

    [Fact]
    public async Task TenantAdmin_role_from_one_tenant_grants_no_access_when_acting_in_another_tenant()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        var userId = await SeedUserWithRoleAsync(harness, Demo01, RoleNames.TenantAdmin, RoleAssignmentSource.System);

        // The same user id, but the ambient tenant is now Demo02 — the assignment above only exists
        // under Demo01, so it must not be found, and the cross-tenant caller must see nothing.
        var tenantContext = new TestTenantContext(Demo02, userId);
        await using var db = harness.Database.CreateContext(tenantContext);
        var accessScope = new EmployeeAccessScopeService(db, tenantContext);

        var predicate = await accessScope.BuildPredicateAsync(new DateOnly(2026, 3, 4));
        var visible = await db.Employees.Where(predicate).CountAsync();

        Assert.Equal(0, visible);
    }

    private static async Task<Guid> SeedUserWithRoleAsync(
        OrganizationTestHarness harness, Guid tenantId, string roleName, RoleAssignmentSource AssignmentSourceForTest, Guid? linkedEmployeeId = null)
    {
        var userId = Guid.NewGuid();
        await using var setup = harness.Database.CreateContext(new TestTenantContext(tenantId));
        setup.Users.Add(new User { Id = userId, TenantId = tenantId, Email = $"{roleName.ToLowerInvariant()}-{userId:N}@test.invalid", FirstName = roleName, LastName = "Test", IsActive = true });
        setup.UserRoles.Add(new UserRole
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            RoleId = SeedData.RoleId(roleName),
            EffectiveFrom = new DateOnly(2026, 1, 1),
            AssignmentSource = AssignmentSourceForTest,
        });
        if (linkedEmployeeId is Guid employeeId)
        {
            // AccountEmployeeCurrentLink.LinkId is a foreign key into AccountEmployeeLinkEvents (the
            // event that created it), per DatabaseSeeder's own account-link seeding — both rows are
            // required together.
            var linkId = Guid.NewGuid();
            setup.AccountEmployeeLinkEvents.Add(new AccountEmployeeLinkEvent
            {
                Id = linkId,
                TenantId = tenantId,
                SubjectUserId = userId,
                ActorUserId = userId,
                Sequence = 1,
                Operation = "Link",
                NewLinkId = linkId,
                AfterEmployeeId = employeeId,
                OccurredAtUtc = DateTime.UtcNow,
                Reason = "Test setup",
                CorrelationId = "employee-access-scope-tests",
            });
            setup.AccountEmployeeCurrentLinks.Add(new AccountEmployeeCurrentLink { LinkId = linkId, TenantId = tenantId, UserId = userId, EmployeeId = employeeId });
        }
        await setup.SaveChangesAsync();
        return userId;
    }
}
