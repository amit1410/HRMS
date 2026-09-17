using HRMS.Application.Abstractions;
using HRMS.Application.Services;
using HRMS.Domain.Authorization;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit.Sdk;

namespace HRMS.Tests;

[Collection("Attendance MySQL")]
public sealed class MySqlLeaveScopeAuthorizationIntegrationTests
{
    [Fact]
    public async Task Real_mysql_leave_scope_predicate_preserves_or_and_union_without_guid_collection_parameters()
    {
        var connection = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) throw SkipException.ForSkip("MySQL Leave scope tests not executed: HRMS_MYSQL_TEST_CONNECTION is absent.");

        var fixture = new MySqlLeaveLifecycleIntegrationTests.Fixture(connection);
        var userId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var roleA = Random.Shared.Next(100_000, 900_000);
        var roleB = roleA + 1;
        var it = Guid.NewGuid();
        var hr = Guid.NewGuid();
        var finance = Guid.NewGuid();
        var noida = Guid.NewGuid();
        var gurgaon = Guid.NewGuid();
        try
        {
            await fixture.SeedAsync();
            await using (var setup = fixture.CreateContext(new TestTenantContext()))
            {
                var permission = await setup.Permissions.SingleAsync(x => x.Name == Permissions.Leave.Approve);
                setup.Users.Add(new User { Id = userId, TenantId = fixture.TenantId, Email = $"leave-scope-{userId:N}@test.invalid", FirstName = "Leave", LastName = "Scope", IsActive = true });
                setup.Employees.Add(new Employee { Id = employeeId, TenantId = fixture.TenantId, EmployeeCode = $"LS{employeeId:N}"[..10], FirstName = "Leave", LastName = "Scope", Email = $"leave-scope-employee-{employeeId:N}@test.invalid", DateOfJoining = new(2020, 1, 1), Status = EmployeeStatus.Active });
                setup.Departments.AddRange(
                    new Department { Id = it, TenantId = fixture.TenantId, Code = $"IT{it:N}"[..8], Name = "IT" },
                    new Department { Id = hr, TenantId = fixture.TenantId, Code = $"HR{hr:N}"[..8], Name = "HR" },
                    new Department { Id = finance, TenantId = fixture.TenantId, Code = $"FN{finance:N}"[..8], Name = "Finance" });
                setup.WorkLocations.AddRange(
                    new WorkLocation { Id = noida, TenantId = fixture.TenantId, Code = $"NO{noida:N}"[..8], Name = "Noida" },
                    new WorkLocation { Id = gurgaon, TenantId = fixture.TenantId, Code = $"GU{gurgaon:N}"[..8], Name = "Gurgaon" });
                setup.Roles.AddRange(
                    new Role { Id = roleA, Name = $"Leave Scope A {roleA}" },
                    new Role { Id = roleB, Name = $"Leave Scope B {roleB}" });
                var assignmentA = new UserRole { Id = Guid.NewGuid(), TenantId = fixture.TenantId, UserId = userId, RoleId = roleA, EffectiveFrom = new(2026, 1, 1) };
                var assignmentB = new UserRole { Id = Guid.NewGuid(), TenantId = fixture.TenantId, UserId = userId, RoleId = roleB, EffectiveFrom = new(2026, 1, 1) };
                setup.UserRoles.AddRange(assignmentA, assignmentB);
                setup.RolePermissions.AddRange(new RolePermission { RoleId = roleA, PermissionId = permission.Id }, new RolePermission { RoleId = roleB, PermissionId = permission.Id });
                AddEmployee(setup, fixture.TenantId, "IT Noida", it, noida);
                AddEmployee(setup, fixture.TenantId, "HR Gurgaon", hr, gurgaon);
                AddEmployee(setup, fixture.TenantId, "IT Gurgaon", it, gurgaon);
                AddEmployee(setup, fixture.TenantId, "HR Noida", hr, noida);
                AddEmployee(setup, fixture.TenantId, "Finance Noida", finance, noida);
                setup.UserRoleAssignmentScopes.AddRange(
                    new UserRoleAssignmentScope { Id = Guid.NewGuid(), TenantId = fixture.TenantId, UserRoleAssignmentId = assignmentA.Id, ScopeType = RoleScopeType.Department, ScopeEntityId = it },
                    new UserRoleAssignmentScope { Id = Guid.NewGuid(), TenantId = fixture.TenantId, UserRoleAssignmentId = assignmentA.Id, ScopeType = RoleScopeType.Location, ScopeEntityId = noida },
                    new UserRoleAssignmentScope { Id = Guid.NewGuid(), TenantId = fixture.TenantId, UserRoleAssignmentId = assignmentB.Id, ScopeType = RoleScopeType.Department, ScopeEntityId = hr },
                    new UserRoleAssignmentScope { Id = Guid.NewGuid(), TenantId = fixture.TenantId, UserRoleAssignmentId = assignmentB.Id, ScopeType = RoleScopeType.Location, ScopeEntityId = gurgaon });
                await setup.SaveChangesAsync();
            }

            await using var db = fixture.CreateContext(new TestTenantContext(fixture.TenantId, userId));
            var tenant = new TestTenantContext(fixture.TenantId, userId);
            var authorization = new LeaveAuthorizationService(
                db,
                tenant,
                new EmployeeIdentityResolver(db, tenant),
                new EmployeeAccessScopeService(db, tenant),
                new EmployeeManagerResolver(db, tenant));
            var predicate = await authorization.BuildEmployeePredicateAsync(Permissions.Leave.Approve, false, true, new(2026, 9, 17));
            Assert.True(predicate.Succeeded, predicate.Message);
            var names = await db.Employees.Where(predicate.Value!).Select(x => x.FirstName).ToListAsync();
            Assert.Contains("IT Noida", names);
            Assert.Contains("HR Gurgaon", names);
            Assert.DoesNotContain("IT Gurgaon", names);
            Assert.DoesNotContain("HR Noida", names);
            Assert.DoesNotContain("Finance Noida", names);
        }
        finally
        {
            await using (var cleanup = fixture.CreateContext(new TestTenantContext()))
            {
                await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `UserRoleAssignmentScopes` WHERE `TenantId` = {fixture.TenantId}");
            }
            await fixture.CleanupAttendanceAsync();
            await fixture.CleanupAsync();
            await using (var cleanup = fixture.CreateContext(new TestTenantContext()))
            {
                await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `RolePermissions` WHERE `RoleId` IN ({roleA}, {roleB})");
                await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `Roles` WHERE `Id` IN ({roleA}, {roleB})");
            }
        }
    }

    private static void AddEmployee(HRMS.Infrastructure.Persistence.HrmsDbContext db, Guid tenantId, string name, Guid departmentId, Guid locationId)
    {
        var id = Guid.NewGuid();
        db.Employees.Add(new Employee { Id = id, TenantId = tenantId, EmployeeCode = $"L{id:N}"[..10], FirstName = name, LastName = "Employee", Email = $"{id:N}@test.invalid", DateOfJoining = new(2020, 1, 1), Status = EmployeeStatus.Active });
        db.EmployeeEmploymentHistory.Add(new EmployeeEmploymentHistory { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = id, EffectiveFrom = new(2026, 1, 1), DepartmentId = departmentId, WorkLocationId = locationId, EmploymentStatus = EmployeeStatus.Active });
    }
}
