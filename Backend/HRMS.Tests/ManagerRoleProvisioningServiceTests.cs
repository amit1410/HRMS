using HRMS.Application.Services;
using HRMS.Domain.Authorization;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HRMS.Tests;

public sealed class ManagerRoleProvisioningServiceTests
{
    [Fact]
    public async Task Existing_effective_reporting_relationship_missing_manager_assignment_is_reconciled()
    {
        using var fixture = await OrganizationTestHarness.CreateAsync();
        var tenantId = SeedData.TenantIds.Demo01;
        var managerEmployeeId = Guid.NewGuid();
        var subordinateEmployeeId = Guid.NewGuid();
        var managerUserId = Guid.NewGuid();

        await using (var db = fixture.CreateContext())
        {
            db.Users.Add(new User { Id = managerUserId, TenantId = tenantId, Email = "legacy-manager@test.invalid", FirstName = "Legacy", LastName = "Manager", PasswordHash = "test", IsActive = true });
            db.Employees.AddRange(
                new Employee { Id = managerEmployeeId, TenantId = tenantId, EmployeeCode = "LEGACY-MGR", FirstName = "Legacy", LastName = "Manager", Email = $"{managerEmployeeId:N}@test.invalid", DateOfJoining = new(2026, 1, 1), Status = EmployeeStatus.Active },
                new Employee { Id = subordinateEmployeeId, TenantId = tenantId, EmployeeCode = "LEGACY-EMP", FirstName = "Legacy", LastName = "Employee", Email = $"{subordinateEmployeeId:N}@test.invalid", DateOfJoining = new(2026, 1, 1), Status = EmployeeStatus.Active, ReportingManagerId = managerEmployeeId });
            AddAccountEmployeeLink(db, tenantId, managerUserId, managerEmployeeId, "legacy-manager-reconciliation");
            AddActiveEmployment(db, tenantId, managerEmployeeId);
            AddActiveEmployment(db, tenantId, subordinateEmployeeId, managerEmployeeId);
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateContext())
        {
            var service = new ManagerRoleProvisioningService(db, NullLogger<ManagerRoleProvisioningService>.Instance, fixture.Clock);
            var result = await service.ReconcileManagerAccessAsync(tenantId, managerEmployeeId);
            await db.SaveChangesAsync();

            Assert.True(result.Eligible);
            Assert.True(result.AssignmentCreated);
            var assignment = await db.UserRoles.IgnoreQueryFilters().SingleAsync(x =>
                x.TenantId == tenantId && x.UserId == managerUserId && x.RoleId == SeedData.RoleId(RoleNames.Manager));
            Assert.Equal(RoleAssignmentSource.System, assignment.AssignmentSource);
            Assert.True(await db.UserRoleAssignmentEvents.IgnoreQueryFilters().AnyAsync(x => x.AssignmentId == assignment.Id && x.EventType == UserRoleAssignmentEventType.Assigned));

            fixture.TenantContext.UserId = managerUserId;
            var scope = new EmployeeAccessScopeService(db, fixture.TenantContext);
            Assert.True(await scope.CanAccessEmployeeAsync(subordinateEmployeeId, new DateOnly(2026, 3, 4)));
        }
    }

    [Fact]
    public async Task Effective_reporting_relationship_provisions_the_linked_manager_and_is_idempotent()
    {
        using var fixture = await OrganizationTestHarness.CreateAsync();
        var tenantId = SeedData.TenantIds.Demo01;
        var managerEmployeeId = Guid.NewGuid();
        var subordinateEmployeeId = Guid.NewGuid();
        var secondSubordinateEmployeeId = Guid.NewGuid();
        var managerUserId = Guid.NewGuid();

        await using (var db = fixture.CreateContext())
        {
            db.Users.Add(new User { Id = managerUserId, TenantId = tenantId, Email = "manager@test.invalid", FirstName = "Manager", LastName = "Test", PasswordHash = "test", IsActive = true });
            db.Employees.AddRange(
                new Employee { Id = managerEmployeeId, TenantId = tenantId, EmployeeCode = "MGR-TEST", FirstName = "Manager", LastName = "Test", Email = "manager@test.invalid", DateOfJoining = new DateOnly(2026, 1, 1), Status = EmployeeStatus.Active },
                new Employee { Id = subordinateEmployeeId, TenantId = tenantId, EmployeeCode = "EMP-TEST", FirstName = "Employee", LastName = "Test", Email = "employee@test.invalid", DateOfJoining = new DateOnly(2026, 1, 1), Status = EmployeeStatus.Active, ReportingManagerId = managerEmployeeId },
                new Employee { Id = secondSubordinateEmployeeId, TenantId = tenantId, EmployeeCode = "EMP-TEST-2", FirstName = "Second", LastName = "Employee", Email = "employee2@test.invalid", DateOfJoining = new DateOnly(2026, 1, 1), Status = EmployeeStatus.Active, ReportingManagerId = managerEmployeeId });
            AddAccountEmployeeLink(db, tenantId, managerUserId, managerEmployeeId, "manager-role-provisioning-test-1");
            AddActiveEmployment(db, tenantId, managerEmployeeId);
            AddActiveEmployment(db, tenantId, subordinateEmployeeId, managerEmployeeId);
            AddActiveEmployment(db, tenantId, secondSubordinateEmployeeId, managerEmployeeId);
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateContext())
        {
            var service = new ManagerRoleProvisioningService(db, NullLogger<ManagerRoleProvisioningService>.Instance, fixture.Clock);
            await service.EnsureForManagerEmployeeAsync(tenantId, managerEmployeeId);
            await db.SaveChangesAsync();
            await service.EnsureForManagerEmployeeAsync(tenantId, managerEmployeeId);
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateContext())
        {
            var managerRoleId = SeedData.RoleId(RoleNames.Manager);
            var assignments = await db.UserRoles.IgnoreQueryFilters().Where(x => x.TenantId == tenantId && x.UserId == managerUserId && x.RoleId == managerRoleId).ToListAsync();
            Assert.Single(assignments);
            Assert.Equal(RoleAssignmentSource.System, assignments[0].AssignmentSource);
            Assert.Equal(new DateOnly(2026, 3, 4), assignments[0].EffectiveFrom);
            Assert.True(await db.UserRoleAssignmentEvents.IgnoreQueryFilters().AnyAsync(x => x.AssignmentId == assignments[0].Id && x.EventType == UserRoleAssignmentEventType.Assigned));

            fixture.TenantContext.UserId = managerUserId;
            var scope = new EmployeeAccessScopeService(db, fixture.TenantContext);
            Assert.True(await scope.CanAccessEmployeeAsync(subordinateEmployeeId, new DateOnly(2026, 3, 4)));
        }
    }

    [Fact]
    public async Task Manager_without_an_effective_subordinate_does_not_receive_manager_role()
    {
        using var fixture = await OrganizationTestHarness.CreateAsync();
        var tenantId = SeedData.TenantIds.Demo01;
        var managerEmployeeId = Guid.NewGuid();
        var managerUserId = Guid.NewGuid();

        await using (var db = fixture.CreateContext())
        {
            db.Users.Add(new User { Id = managerUserId, TenantId = tenantId, Email = "manager2@test.invalid", FirstName = "Manager", LastName = "Test", PasswordHash = "test", IsActive = true });
            db.Employees.Add(new Employee { Id = managerEmployeeId, TenantId = tenantId, EmployeeCode = "MGR-TEST-2", FirstName = "Manager", LastName = "Test", Email = "manager2@test.invalid", DateOfJoining = new DateOnly(2026, 1, 1), Status = EmployeeStatus.Active });
            AddAccountEmployeeLink(db, tenantId, managerUserId, managerEmployeeId, "manager-role-provisioning-test-2");
            AddActiveEmployment(db, tenantId, managerEmployeeId);
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateContext())
        {
            var service = new ManagerRoleProvisioningService(db, NullLogger<ManagerRoleProvisioningService>.Instance, fixture.Clock);
            await service.EnsureForManagerEmployeeAsync(tenantId, managerEmployeeId);
            await db.SaveChangesAsync();
            Assert.False(await db.UserRoles.IgnoreQueryFilters().AnyAsync(x => x.TenantId == tenantId && x.UserId == managerUserId && x.RoleId == SeedData.RoleId(RoleNames.Manager)));
        }
    }

    [Fact]
    public async Task Future_and_expired_reporting_relationships_do_not_provision_manager_access()
    {
        using var fixture = await OrganizationTestHarness.CreateAsync();
        var tenantId = SeedData.TenantIds.Demo01;
        var managerEmployeeId = Guid.NewGuid();
        var futureEmployeeId = Guid.NewGuid();
        var expiredEmployeeId = Guid.NewGuid();
        var managerUserId = Guid.NewGuid();

        await using (var db = fixture.CreateContext())
        {
            db.Users.Add(new User { Id = managerUserId, TenantId = tenantId, Email = "dated-manager@test.invalid", FirstName = "Dated", LastName = "Manager", PasswordHash = "test", IsActive = true });
            db.Employees.AddRange(
                new Employee { Id = managerEmployeeId, TenantId = tenantId, EmployeeCode = "DATED-MGR", FirstName = "Dated", LastName = "Manager", Email = $"{managerEmployeeId:N}@test.invalid", DateOfJoining = new(2026, 1, 1), Status = EmployeeStatus.Active },
                new Employee { Id = futureEmployeeId, TenantId = tenantId, EmployeeCode = "FUTURE-EMP", FirstName = "Future", LastName = "Employee", Email = $"{futureEmployeeId:N}@test.invalid", DateOfJoining = new(2026, 1, 1), Status = EmployeeStatus.Active },
                new Employee { Id = expiredEmployeeId, TenantId = tenantId, EmployeeCode = "EXPIRED-EMP", FirstName = "Expired", LastName = "Employee", Email = $"{expiredEmployeeId:N}@test.invalid", DateOfJoining = new(2026, 1, 1), Status = EmployeeStatus.Active });
            AddAccountEmployeeLink(db, tenantId, managerUserId, managerEmployeeId, "dated-manager-reconciliation");
            AddActiveEmployment(db, tenantId, managerEmployeeId);
            AddActiveEmployment(db, tenantId, futureEmployeeId, managerEmployeeId, new DateOnly(2026, 4, 1));
            AddActiveEmployment(db, tenantId, expiredEmployeeId, managerEmployeeId, new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 3));
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateContext())
        {
            var service = new ManagerRoleProvisioningService(db, NullLogger<ManagerRoleProvisioningService>.Instance, fixture.Clock);
            var result = await service.ReconcileManagerAccessAsync(tenantId, managerEmployeeId);
            await db.SaveChangesAsync();
            Assert.False(result.Eligible);
            Assert.False(await db.UserRoles.IgnoreQueryFilters().AnyAsync(x => x.TenantId == tenantId && x.UserId == managerUserId && x.RoleId == SeedData.RoleId(RoleNames.Manager)));
        }
    }

    private static void AddActiveEmployment(
        HrmsDbContext db,
        Guid tenantId,
        Guid employeeId,
        Guid? managerId = null,
        DateOnly? effectiveFrom = null,
        DateOnly? effectiveTo = null)
    {
        db.EmployeeEmploymentHistory.Add(new EmployeeEmploymentHistory
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = employeeId,
            ManagerId = managerId,
            EffectiveFrom = effectiveFrom ?? new DateOnly(2026, 1, 1),
            EffectiveTo = effectiveTo,
            EmploymentStatus = EmployeeStatus.Active,
        });
    }

    private static void AddAccountEmployeeLink(
        HrmsDbContext db,
        Guid tenantId,
        Guid userId,
        Guid employeeId,
        string correlationId)
    {
        var linkId = Guid.NewGuid();
        db.AccountEmployeeLinkEvents.Add(new AccountEmployeeLinkEvent
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
            CorrelationId = correlationId,
        });
        db.AccountEmployeeCurrentLinks.Add(new AccountEmployeeCurrentLink
        {
            LinkId = linkId,
            TenantId = tenantId,
            UserId = userId,
            EmployeeId = employeeId,
        });
    }
}
