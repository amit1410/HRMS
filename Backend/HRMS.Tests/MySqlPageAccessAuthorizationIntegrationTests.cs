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

[Collection("Attendance MySQL")]
public sealed class MySqlPageAccessAuthorizationIntegrationTests
{
    [Fact]
    public async Task Real_mysql_page_access_uses_only_effective_roles_and_preserves_tenant_isolation()
    {
        var connection = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) throw SkipException.ForSkip("MySQL Page Access tests not executed: HRMS_MYSQL_TEST_CONNECTION is absent.");
        var fixture = new MySqlLeaveLifecycleIntegrationTests.Fixture(connection);
        var activeRoleId = Random.Shared.Next(100_000, 900_000);
        var futureRoleId = activeRoleId + 1;
        var expiredRoleId = activeRoleId + 2;
        try
        {
            await RemoveStaleLifecycleTenantsAsync(connection);
            await fixture.SeedAsync();
            await using (var setup = fixture.CreateContext(new TestTenantContext()))
            {
                var view = await GetOrAddPermissionAsync(setup, Permissions.PageAccess.View);
                var manage = await GetOrAddPermissionAsync(setup, Permissions.PageAccess.Manage);
                var managerAccess = await GetOrAddPermissionAsync(setup, Permissions.Attendance.MonthlyViewTeam);
                setup.Roles.AddRange(
                    new Role { Id = activeRoleId, Name = $"MySQL Active {activeRoleId}" },
                    new Role { Id = futureRoleId, Name = $"MySQL Future {futureRoleId}" },
                    new Role { Id = expiredRoleId, Name = $"MySQL Expired {expiredRoleId}" });
                setup.RolePermissions.AddRange(
                    new RolePermission { RoleId = activeRoleId, PermissionId = view.Id },
                    new RolePermission { RoleId = activeRoleId, PermissionId = managerAccess.Id },
                    new RolePermission { RoleId = futureRoleId, PermissionId = manage.Id },
                    new RolePermission { RoleId = expiredRoleId, PermissionId = manage.Id });
                setup.UserRoles.AddRange(
                    new UserRole { TenantId = fixture.TenantId, UserId = fixture.ManagerUserId, RoleId = activeRoleId, EffectiveFrom = new(2026, 1, 1) },
                    new UserRole { TenantId = fixture.TenantId, UserId = fixture.ManagerUserId, RoleId = futureRoleId, EffectiveFrom = new(2027, 1, 1) },
                    new UserRole { TenantId = fixture.TenantId, UserId = fixture.ManagerUserId, RoleId = expiredRoleId, EffectiveFrom = new(2025, 1, 1), EffectiveTo = new(2025, 12, 31) });
                await setup.SaveChangesAsync();
            }

            await using (var db = fixture.CreateContext(fixture.ManagerTenant))
            {
                var service = new PageAccessService(db, fixture.ManagerTenant, new RoleResolutionService(db), new FixedClock(new DateTimeOffset(2026, 9, 17, 12, 0, 0, TimeSpan.Zero)));
                var roles = await service.GetRolesAsync();
                Assert.Contains(roles.Value!, role => role.Id == activeRoleId);

                var beforeUpdate = await service.GetMatrixAsync(activeRoleId);
                Assert.True(beforeUpdate.Value!.Pages.Single(x => x.Code == "ADMIN.PAGE_ACCESS").Actions.Single(x => x.Permission == Permissions.PageAccess.View).Allowed);
                Assert.False(beforeUpdate.Value.Pages.Single(x => x.Code == "ADMIN.PAGE_ACCESS").Actions.Single(x => x.Permission == Permissions.PageAccess.Manage).Allowed);

                var updated = await service.UpdateAsync(activeRoleId, new([Permissions.PageAccess.View, Permissions.PageAccess.Manage, Permissions.Attendance.MonthlyViewTeam]));
                Assert.True(updated.Succeeded);
                Assert.Contains(Permissions.PageAccess.Manage, updated.Value!.GrantedPermissions);
                var afterGrant = await db.AuthorizationConfigurationEvents.AsNoTracking().Where(x => x.RoleId == activeRoleId).ToListAsync();
                Assert.Single(afterGrant, x => x.EventType == AuthorizationConfigurationEventType.RolePermissionGranted && x.PermissionCode == Permissions.PageAccess.Manage && x.ActorUserId == fixture.ManagerUserId);
                var unchanged = await service.UpdateAsync(activeRoleId, new([Permissions.PageAccess.View, Permissions.PageAccess.Manage, Permissions.Attendance.MonthlyViewTeam]));
                Assert.True(unchanged.Succeeded);
                Assert.Equal(afterGrant.Count, await db.AuthorizationConfigurationEvents.CountAsync(x => x.RoleId == activeRoleId));
                var revoked = await service.UpdateAsync(activeRoleId, new([Permissions.PageAccess.View, Permissions.Attendance.MonthlyViewTeam]));
                Assert.True(revoked.Succeeded);
                Assert.Single(await db.AuthorizationConfigurationEvents.AsNoTracking().Where(x => x.RoleId == activeRoleId && x.EventType == AuthorizationConfigurationEventType.RolePermissionRevoked).ToListAsync());
                var history = await service.GetHistoryAsync(new(RoleId: activeRoleId, PageSize: 10));
                Assert.True(history.Succeeded);
                Assert.Equal(2, history.Value!.TotalCount);
                await service.UpdateAsync(activeRoleId, new([Permissions.PageAccess.View, Permissions.PageAccess.Manage, Permissions.Attendance.MonthlyViewTeam]));

                var navigation = await service.GetNavigationAsync();
                Assert.Contains(navigation.Value!, module => module.Children.Any(page => page.Route == "/page-access-management"));

                var preview = await service.GetUserPreviewAsync(fixture.ManagerUserId);
                Assert.True(preview.Succeeded);
                Assert.Contains(preview.Value!.Roles, role => role.RoleId == activeRoleId);
                Assert.Contains(Permissions.PageAccess.View, preview.Value.Permissions);
                Assert.Contains(Permissions.PageAccess.Manage, preview.Value.Permissions);
                Assert.True(preview.Value.HasManagerAccess);
                Assert.Contains(preview.Value.Pages, page => page.Route == "/page-access-management");
            }

            await using (var otherTenant = fixture.CreateContext(new TestTenantContext(fixture.OtherTenantId, fixture.ManagerUserId)))
            {
                var service = new PageAccessService(otherTenant, new TestTenantContext(fixture.OtherTenantId, fixture.ManagerUserId), new RoleResolutionService(otherTenant), new FixedClock(new DateTimeOffset(2026, 9, 17, 12, 0, 0, TimeSpan.Zero)));
                var preview = await service.GetUserPreviewAsync(fixture.ManagerUserId);
                Assert.True(preview.Succeeded);
                Assert.Empty(preview.Value!.Roles);
                Assert.Empty(preview.Value.Permissions);
                Assert.Empty(preview.Value.Pages);
            }
        }
        finally
        {
            await using (var cleanup = fixture.CreateContext(new TestTenantContext()))
                await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `UserRoleAssignmentScopes` WHERE `TenantId` IN ({fixture.TenantId}, {fixture.OtherTenantId})");
            await fixture.CleanupAsync();
            await DeleteRolesAsync(connection, activeRoleId, futureRoleId, expiredRoleId);
        }
    }

    [Fact]
    public async Task Real_mysql_scope_predicate_handles_multiple_values_and_keeps_assignment_dimensions_isolated()
    {
        var connection = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) throw SkipException.ForSkip("MySQL employee-scope tests not executed: HRMS_MYSQL_TEST_CONNECTION is absent.");
        var fixture = new MySqlLeaveLifecycleIntegrationTests.Fixture(connection);
        var scopedUserId = Guid.NewGuid();
        var multiValueUserId = Guid.NewGuid();
        var scopedRoleId = Random.Shared.Next(100_000, 900_000);
        var scopedRole2Id = scopedRoleId + 1;
        var multiValueRoleId = scopedRoleId + 2;
        var itId = Guid.NewGuid();
        var hrId = Guid.NewGuid();
        var financeId = Guid.NewGuid();
        var noidaId = Guid.NewGuid();
        var gurgaonId = Guid.NewGuid();
        try
        {
            await RemoveStaleLifecycleTenantsAsync(connection);
            await fixture.SeedAsync();
            await using (var setup = fixture.CreateContext(new TestTenantContext()))
            {
                setup.Users.AddRange(
                    new User { Id = scopedUserId, TenantId = fixture.TenantId, Email = $"scope-{scopedUserId:N}@test.invalid", FirstName = "Scope", LastName = "User", IsActive = true },
                    new User { Id = multiValueUserId, TenantId = fixture.TenantId, Email = $"multi-{multiValueUserId:N}@test.invalid", FirstName = "Multi", LastName = "User", IsActive = true });
                setup.Departments.AddRange(
                    new Department { Id = itId, TenantId = fixture.TenantId, Code = $"IT{itId:N}"[..8], Name = "IT" },
                    new Department { Id = hrId, TenantId = fixture.TenantId, Code = $"HR{hrId:N}"[..8], Name = "HR" },
                    new Department { Id = financeId, TenantId = fixture.TenantId, Code = $"FN{financeId:N}"[..8], Name = "Finance" });
                setup.WorkLocations.AddRange(
                    new WorkLocation { Id = noidaId, TenantId = fixture.TenantId, Code = $"NO{noidaId:N}"[..8], Name = "Noida" },
                    new WorkLocation { Id = gurgaonId, TenantId = fixture.TenantId, Code = $"GU{gurgaonId:N}"[..8], Name = "Gurgaon" });
                setup.Roles.AddRange(
                    new Role { Id = scopedRoleId, Name = $"MySQL Scoped {scopedRoleId}" },
                    new Role { Id = scopedRole2Id, Name = $"MySQL Scoped {scopedRole2Id}" },
                    new Role { Id = multiValueRoleId, Name = $"MySQL Multi {multiValueRoleId}" });
                setup.UserRoles.AddRange(
                    new UserRole { Id = Guid.NewGuid(), TenantId = fixture.TenantId, UserId = scopedUserId, RoleId = scopedRoleId, EffectiveFrom = new(2026, 1, 1) },
                    new UserRole { Id = Guid.NewGuid(), TenantId = fixture.TenantId, UserId = scopedUserId, RoleId = scopedRole2Id, EffectiveFrom = new(2026, 1, 1) },
                    new UserRole { Id = Guid.NewGuid(), TenantId = fixture.TenantId, UserId = multiValueUserId, RoleId = multiValueRoleId, EffectiveFrom = new(2026, 1, 1) });
                await setup.SaveChangesAsync();

                var scopedAssignments = await setup.UserRoles.IgnoreQueryFilters().Where(x => x.TenantId == fixture.TenantId && x.UserId == scopedUserId).OrderBy(x => x.Id).ToListAsync();
                setup.UserRoleAssignmentScopes.AddRange(
                    new() { Id = Guid.NewGuid(), TenantId = fixture.TenantId, UserRoleAssignmentId = scopedAssignments[0].Id, ScopeType = RoleScopeType.Department, ScopeEntityId = itId },
                    new() { Id = Guid.NewGuid(), TenantId = fixture.TenantId, UserRoleAssignmentId = scopedAssignments[0].Id, ScopeType = RoleScopeType.Location, ScopeEntityId = noidaId },
                    new() { Id = Guid.NewGuid(), TenantId = fixture.TenantId, UserRoleAssignmentId = scopedAssignments[1].Id, ScopeType = RoleScopeType.Department, ScopeEntityId = hrId },
                    new() { Id = Guid.NewGuid(), TenantId = fixture.TenantId, UserRoleAssignmentId = scopedAssignments[1].Id, ScopeType = RoleScopeType.Location, ScopeEntityId = gurgaonId });
                var multiAssignment = await setup.UserRoles.IgnoreQueryFilters().SingleAsync(x => x.TenantId == fixture.TenantId && x.UserId == multiValueUserId);
                setup.UserRoleAssignmentScopes.AddRange(
                    new() { Id = Guid.NewGuid(), TenantId = fixture.TenantId, UserRoleAssignmentId = multiAssignment.Id, ScopeType = RoleScopeType.Department, ScopeEntityId = itId },
                    new() { Id = Guid.NewGuid(), TenantId = fixture.TenantId, UserRoleAssignmentId = multiAssignment.Id, ScopeType = RoleScopeType.Department, ScopeEntityId = hrId },
                    new() { Id = Guid.NewGuid(), TenantId = fixture.TenantId, UserRoleAssignmentId = multiAssignment.Id, ScopeType = RoleScopeType.Location, ScopeEntityId = noidaId });

                var itNoida = AddEmployee(setup, fixture.TenantId, "IT Noida", itId, noidaId);
                var hrGurgaon = AddEmployee(setup, fixture.TenantId, "HR Gurgaon", hrId, gurgaonId);
                var itGurgaon = AddEmployee(setup, fixture.TenantId, "IT Gurgaon", itId, gurgaonId);
                var hrNoida = AddEmployee(setup, fixture.TenantId, "HR Noida", hrId, noidaId);
                var changing = AddEmployee(setup, fixture.TenantId, "Finance then IT", financeId, noidaId);
                setup.EmployeeEmploymentHistory.Add(new EmployeeEmploymentHistory { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = changing.Id, EffectiveFrom = new(2026, 10, 1), DepartmentId = itId, WorkLocationId = noidaId, EmploymentStatus = EmployeeStatus.Active });
                await setup.SaveChangesAsync();

                await using var scopedDb = fixture.CreateContext(new TestTenantContext(fixture.TenantId, scopedUserId));
                var scoped = new EmployeeAccessScopeService(scopedDb, new TestTenantContext(fixture.TenantId, scopedUserId));
                var dayBefore = await scoped.BuildPredicateAsync(new(2026, 9, 30));
                var effective = await scoped.BuildPredicateAsync(new(2026, 10, 1));
                Assert.DoesNotContain(changing.Id, await scopedDb.Employees.Where(dayBefore).Select(x => x.Id).ToListAsync());
                Assert.Contains(changing.Id, await scopedDb.Employees.Where(effective).Select(x => x.Id).ToListAsync());
                var scopedIds = await scopedDb.Employees.Where(effective).Select(x => x.Id).ToListAsync();
                Assert.Contains(itNoida.Id, scopedIds);
                Assert.Contains(hrGurgaon.Id, scopedIds);
                Assert.DoesNotContain(itGurgaon.Id, scopedIds);
                Assert.DoesNotContain(hrNoida.Id, scopedIds);
                Assert.DoesNotContain(scopedIds, id => id == fixture.EmployeeId);
                Assert.DoesNotContain(scopedIds, id => id == fixture.ManagerId);

                await using var multiDb = fixture.CreateContext(new TestTenantContext(fixture.TenantId, multiValueUserId));
                var multi = new EmployeeAccessScopeService(multiDb, new TestTenantContext(fixture.TenantId, multiValueUserId));
                var multiIds = await multiDb.Employees.Where(await multi.BuildPredicateAsync(new(2026, 9, 17))).Select(x => x.Id).ToListAsync();
                Assert.Contains(multiIds, id => id != fixture.EmployeeId);
                Assert.DoesNotContain(multiIds, id => id == fixture.ManagerId);
            }
        }
        finally
        {
            await using (var cleanup = fixture.CreateContext(new TestTenantContext()))
            {
                await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `WorkLocations` WHERE `TenantId` IN ({fixture.TenantId}, {fixture.OtherTenantId})");
                await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `Departments` WHERE `TenantId` IN ({fixture.TenantId}, {fixture.OtherTenantId})");
                await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `UserRoleAssignmentScopes` WHERE `TenantId` IN ({fixture.TenantId}, {fixture.OtherTenantId})");
            }
            await fixture.CleanupAsync();
            await DeleteRolesAsync(connection, scopedRoleId, scopedRole2Id, multiValueRoleId);
        }
    }

    private static async Task<Permission> GetOrAddPermissionAsync(HrmsDbContext db, string name)
    {
        var permission = await db.Permissions.SingleOrDefaultAsync(x => x.Name == name);
        if (permission is not null) return permission;
        permission = new Permission { Id = SeedData.PermissionId(name), Name = name, Description = name.Replace('.', ' ') };
        db.Permissions.Add(permission);
        await db.SaveChangesAsync();
        return permission;
    }

    private static async Task RemoveStaleLifecycleTenantsAsync(string connection)
    {
        var fixture = new MySqlLeaveLifecycleIntegrationTests.Fixture(connection);
        await using var db = fixture.CreateContext(new TestTenantContext());
        var staleIds = await db.Tenants.IgnoreQueryFilters().Where(x => x.TenantName == "MySQL Leave Lifecycle").Select(x => x.Id).ToListAsync();
        foreach (var tenantId in staleIds)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `LeaveReminderDeliveries` WHERE `TenantId` = {tenantId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `LeaveBalanceImportRows` WHERE `TenantId` = {tenantId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `LeaveBalanceImportBatches` WHERE `TenantId` = {tenantId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `LeaveAccrualOccurrences` WHERE `TenantId` = {tenantId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `LeavePeriodCloseOccurrences` WHERE `TenantId` = {tenantId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `LeaveBalanceReservationAllocations` WHERE `TenantId` = {tenantId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `LeaveEntitlementGrants` WHERE `TenantId` = {tenantId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `LeaveRequestEvents` WHERE `TenantId` = {tenantId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `LeaveRequestDays` WHERE `TenantId` = {tenantId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `LeaveBalanceTransactions` WHERE `TenantId` = {tenantId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `LeaveRequests` WHERE `TenantId` = {tenantId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `AccountEmployeeCurrentLinks` WHERE `TenantId` = {tenantId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `AccountEmployeeLinkEvents` WHERE `TenantId` = {tenantId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `UserRoleAssignmentScopes` WHERE `TenantId` = {tenantId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `EmployeeLeaveBalances` WHERE `TenantId` = {tenantId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `LeavePolicyCancellationRules` WHERE `TenantId` = {tenantId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `LeavePolicyEntitlementRules` WHERE `TenantId` = {tenantId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `LeavePolicyRules` WHERE `TenantId` = {tenantId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `LeavePolicyVersions` WHERE `TenantId` = {tenantId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `LeavePolicies` WHERE `TenantId` = {tenantId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `LeavePeriods` WHERE `TenantId` = {tenantId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `LeaveTypes` WHERE `TenantId` = {tenantId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `UserRoles` WHERE `TenantId` = {tenantId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `EmployeeEmploymentHistory` WHERE `TenantId` = {tenantId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE `Employees` SET `ReportingManagerId` = NULL WHERE `TenantId` = {tenantId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `Employees` WHERE `TenantId` = {tenantId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `Users` WHERE `TenantId` = {tenantId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `WorkLocations` WHERE `TenantId` = {tenantId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `Departments` WHERE `TenantId` = {tenantId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `Tenants` WHERE `Id` = {tenantId}");
        }
    }

    private static async Task DeleteRolesAsync(string connection, params int[] roleIds)
    {
        await using var db = new MySqlLeaveLifecycleIntegrationTests.Fixture(connection).CreateContext(new TestTenantContext());
        var ids = string.Join(",", roleIds);
        await db.Database.ExecuteSqlRawAsync($"DELETE FROM `RolePermissions` WHERE `RoleId` IN ({ids})");
        await db.Database.ExecuteSqlRawAsync($"DELETE FROM `Roles` WHERE `Id` IN ({ids})");
    }

    private static Employee AddEmployee(HrmsDbContext db, Guid tenantId, string name, Guid departmentId, Guid locationId)
    {
        var id = Guid.NewGuid();
        var employee = new Employee { Id = id, TenantId = tenantId, EmployeeCode = $"S{id:N}"[..10], Email = $"{id:N}@test.invalid", FirstName = name, LastName = "Employee", DateOfJoining = new(2020, 1, 1), Status = EmployeeStatus.Active };
        db.Employees.Add(employee);
        db.EmployeeEmploymentHistory.Add(new EmployeeEmploymentHistory { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employee.Id, EffectiveFrom = new(2026, 1, 1), DepartmentId = departmentId, WorkLocationId = locationId, EmploymentStatus = EmployeeStatus.Active });
        return employee;
    }
}
