using HRMS.Domain.Authorization;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Authorization;
using HRMS.Application.Services;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Infrastructure.Security;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Collections.Concurrent;
using DomainPermissions = HRMS.Domain.Authorization.Permissions;

namespace HRMS.Tests;

public class SeedDataTests
{
    private const string VerificationPassword = "Original-password-123!";
    private const string ResetPassword = "Reset-password-456!";

    [Fact]
    public async Task Seed_populates_expected_reference_data_and_demo_tenants()
    {
        using var db = new SqliteInMemoryDatabase();
        await db.SeedAsync();

        using var context = db.CreateContext(new TestTenantContext());

        Assert.Equal(RoleNames.All.Count, await context.Roles.CountAsync());
        Assert.Equal(DomainPermissions.All.Count, await context.Permissions.CountAsync());
        Assert.Equal(2, await context.Tenants.CountAsync());
        Assert.Equal(4, await context.Users.IgnoreQueryFilters().CountAsync());
        Assert.Equal(4, await context.UserRoles.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task Seed_is_idempotent()
    {
        using var db = new SqliteInMemoryDatabase();
        await db.SeedAsync();
        await db.SeedAsync(); // second run must neither duplicate nor throw

        using var context = db.CreateContext(new TestTenantContext());

        Assert.Equal(2, await context.Tenants.CountAsync());
        Assert.Equal(4, await context.Users.IgnoreQueryFilters().CountAsync());
        Assert.Equal(RoleNames.All.Count, await context.Roles.CountAsync());
        Assert.Equal(SeedData.Departments.Count, await context.Departments.IgnoreQueryFilters().CountAsync());
        Assert.Equal(SeedData.Designations.Count, await context.Designations.IgnoreQueryFilters().CountAsync());
        Assert.Equal(SeedData.Employees.Count, await context.Employees.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task Seed_does_not_rewrite_existing_role_permission_configuration()
    {
        using var db = new SqliteInMemoryDatabase();
        await db.SeedAsync();

        using (var context = db.CreateContext(new TestTenantContext()))
        {
            var rolePermission = await context.RolePermissions.SingleAsync(x =>
                x.RoleId == SeedData.RoleId(RoleNames.HRBP) &&
                x.PermissionId == SeedData.PermissionId(DomainPermissions.Confirmation.PolicyView));
            context.RolePermissions.Remove(rolePermission);
            await context.SaveChangesAsync();
        }

        await db.SeedAsync();

        using var verify = db.CreateContext(new TestTenantContext());
        Assert.False(await verify.RolePermissions.AnyAsync(x =>
            x.RoleId == SeedData.RoleId(RoleNames.HRBP) &&
            x.PermissionId == SeedData.PermissionId(DomainPermissions.Confirmation.PolicyView)));
    }

    [Fact]
    public async Task Permission_provisioning_preserves_legacy_id_and_role_assignments_while_adding_missing_names()
    {
        using var db = new SqliteInMemoryDatabase();
        await db.SeedAsync();
        var tenant = new TestTenantContext(SeedData.TenantIds.Demo01, SeedData.Users[0].Id);
        int roleId = SeedData.RoleId(RoleNames.TenantAdmin);
        int grantCount;
        await using (var arrange = db.CreateContext(tenant))
        {
            var scheduler = await arrange.Permissions.SingleAsync(x => x.Name == DomainPermissions.Confirmation.SchedulerView);
            scheduler.Name = "Legacy.PreviousScheduler";
            var canonical = await arrange.Permissions.SingleAsync(x => x.Id == 294);
            canonical.Name = "Confirmation.SchedulerView";
            var welcome = await arrange.Permissions.SingleAsync(x => x.Name == DomainPermissions.WelcomeInvitation.View);
            arrange.Permissions.Remove(welcome);
            await arrange.SaveChangesAsync();
            grantCount = await arrange.RolePermissions.CountAsync();
        }

        await using (var context = db.CreateContext(tenant))
        {
            var provisioner = new HRMS.Infrastructure.Persistence.PermissionCatalogProvisioner(context);
            await provisioner.EnsureProvisionedAsync();
            await provisioner.EnsureProvisionedAsync();
        }

        await using var verify = db.CreateContext(tenant);
        Assert.Equal("Confirmation.SchedulerView", (await verify.Permissions.SingleAsync(x => x.Id == 294)).Name);
        Assert.NotEqual(294, (await verify.Permissions.SingleAsync(x => x.Name == DomainPermissions.Confirmation.DashboardViewAll)).Id);
        Assert.Equal(grantCount, await verify.RolePermissions.CountAsync());
        Assert.True(await verify.RolePermissions.AnyAsync(x => x.RoleId == roleId && x.PermissionId == 294));
        Assert.True(await verify.Permissions.AnyAsync(x => x.Name == DomainPermissions.WelcomeInvitation.View));
        Assert.True(await verify.RolePermissions.AnyAsync(x => x.RoleId == roleId));
    }

    [Fact]
    public async Task Permission_provisioning_reuses_canonical_name_under_legacy_id()
    {
        using var db = new SqliteInMemoryDatabase();
        await db.SeedAsync();
        var tenant = new TestTenantContext(SeedData.TenantIds.Demo01, SeedData.Users[0].Id);
        var expectedName = DomainPermissions.Confirmation.DashboardViewAll;
        var expectedId = SeedData.PermissionId(expectedName);
        await using (var arrange = db.CreateContext(tenant))
        {
            arrange.Permissions.Remove(await arrange.Permissions.SingleAsync(x => x.Id == expectedId));
            arrange.Permissions.Add(new Permission { Id = 999999, Name = expectedName });
            await arrange.SaveChangesAsync();
        }

        await using (var context = db.CreateContext(tenant))
        {
            var provisioner = new HRMS.Infrastructure.Persistence.PermissionCatalogProvisioner(context);
            await provisioner.EnsureProvisionedAsync();
            Assert.Equal(999999, await context.Permissions.Where(x => x.Name == expectedName).Select(x => x.Id).SingleAsync());
        }
    }

    [Fact]
    public async Task Page_access_grants_permission_by_persisted_id_when_it_differs_from_canonical_id()
    {
        using var db = new SqliteInMemoryDatabase();
        await db.SeedAsync();
        var tenantContext = new TestTenantContext(SeedData.TenantIds.Demo01, SeedData.Users[0].Id);
        var roleId = SeedData.RoleId(RoleNames.TenantAdmin);
        const int persistedPermissionId = 999999;
        await using (var arrange = db.CreateContext(tenantContext))
        {
            arrange.Permissions.Remove(await arrange.Permissions.SingleAsync(x => x.Name == DomainPermissions.WelcomeInvitation.View));
            arrange.Permissions.Add(new Permission { Id = persistedPermissionId, Name = DomainPermissions.WelcomeInvitation.View });
            await arrange.SaveChangesAsync();
        }

        await using var context = db.CreateContext(tenantContext);
        var service = new PageAccessService(context, tenantContext, new RoleResolutionService(context), TimeProvider.System,
            permissionCatalogProvisioner: new HRMS.Infrastructure.Persistence.PermissionCatalogProvisioner(context));
        var result = await service.UpdateAsync(roleId, new([DomainPermissions.WelcomeInvitation.View], "Legacy id regression"));

        Assert.True(result.Succeeded, result.Message);
        Assert.True(await context.RolePermissions.AnyAsync(x => x.RoleId == roleId && x.PermissionId == persistedPermissionId));
        Assert.False(await context.RolePermissions.AnyAsync(x => x.RoleId == roleId && x.PermissionId == SeedData.PermissionId(DomainPermissions.WelcomeInvitation.View)));
    }

    [Fact]
    public async Task Permission_provisioning_preserves_existing_rows_even_when_canonical_ids_are_occupied()
    {
        using var db = new SqliteInMemoryDatabase();
        await db.SeedAsync();
        var tenant = new TestTenantContext(SeedData.TenantIds.Demo01, SeedData.Users[0].Id);
        await using (var arrange = db.CreateContext(tenant))
        {
            (await arrange.Permissions.SingleAsync(x => x.Name == DomainPermissions.Confirmation.SchedulerView)).Name = "Legacy.PreviousScheduler";
            var first = await arrange.Permissions.SingleAsync(x => x.Id == 294);
            first.Name = "Confirmation.SchedulerView";
            arrange.Permissions.Remove(await arrange.Permissions.SingleAsync(x => x.Name == DomainPermissions.WelcomeInvitation.Send));
            await arrange.SaveChangesAsync();
        }

        await using (var context = db.CreateContext(tenant))
        {
            var beforeGrants = await context.RolePermissions.AsNoTracking().OrderBy(x => x.RoleId).ThenBy(x => x.PermissionId)
                .Select(x => new { x.RoleId, x.PermissionId }).ToListAsync();
            await new HRMS.Infrastructure.Persistence.PermissionCatalogProvisioner(context).EnsureProvisionedAsync();
            Assert.Equal("Confirmation.SchedulerView", await context.Permissions.Where(x => x.Id == 294).Select(x => x.Name).SingleAsync());
            Assert.True(await context.Permissions.AnyAsync(x => x.Name == DomainPermissions.Confirmation.DashboardViewAll));
            Assert.True(await context.Permissions.AnyAsync(x => x.Name == DomainPermissions.WelcomeInvitation.Send));
            Assert.Equal(beforeGrants, await context.RolePermissions.AsNoTracking().OrderBy(x => x.RoleId).ThenBy(x => x.PermissionId)
                .Select(x => new { x.RoleId, x.PermissionId }).ToListAsync());
        }
    }

    [Fact]
    public async Task Permission_provisioning_inserts_missing_rows_and_is_idempotent()
    {
        using var db = new SqliteInMemoryDatabase();
        await using var context = db.CreateContext(new TestTenantContext());
        context.Database.EnsureCreated();
        var provisioner = new HRMS.Infrastructure.Persistence.PermissionCatalogProvisioner(context);

        await provisioner.EnsureProvisionedAsync();
        var count = await context.Permissions.CountAsync();
        await provisioner.EnsureProvisionedAsync();

        Assert.Equal(DomainPermissions.All.Count, count);
        Assert.Equal(count, await context.Permissions.CountAsync());
        Assert.Equal(DomainPermissions.All.Count, await context.Permissions.Select(x => x.Name).Distinct().CountAsync());
    }

    [Fact]
    public async Task Approved_reconciliation_restores_a_missing_new_permission_and_is_audited()
    {
        using var db = new SqliteInMemoryDatabase();
        await db.SeedAsync();
        var tenantContext = new TestTenantContext(SeedData.TenantIds.Demo01, SeedData.Users[0].Id);

        await using (var arrange = db.CreateContext(new TestTenantContext()))
        {
            var grant = await arrange.RolePermissions.SingleAsync(x =>
                x.RoleId == SeedData.RoleId(RoleNames.TenantAdmin) &&
                x.PermissionId == SeedData.PermissionId(DomainPermissions.WelcomeInvitation.View));
            arrange.RolePermissions.Remove(grant);
            await arrange.SaveChangesAsync();
        }

        await using (var context = db.CreateContext(tenantContext))
        {
            var service = new PageAccessService(context, tenantContext, new RoleResolutionService(context), TimeProvider.System);
            var result = await service.ReconcileApprovedDefaultsAsync(
                SeedData.RoleId(RoleNames.TenantAdmin),
                new PageAccessReconciliationRequest([DomainPermissions.WelcomeInvitation.View], "Provision new communication permission"));

            Assert.True(result.Succeeded, result.Message);
            Assert.Contains(DomainPermissions.WelcomeInvitation.View, result.Value!.GrantedPermissions);
            Assert.Single(await context.AuthorizationConfigurationEvents.Where(x =>
                x.RoleId == SeedData.RoleId(RoleNames.TenantAdmin) &&
                x.PermissionCode == DomainPermissions.WelcomeInvitation.View &&
                x.Action == "Reconciled").ToListAsync());
        }
    }

    [Fact]
    public async Task Approved_reconciliation_is_idempotent_when_permission_is_already_granted()
    {
        using var db = new SqliteInMemoryDatabase();
        await db.SeedAsync();
        var tenantContext = new TestTenantContext(SeedData.TenantIds.Demo01, SeedData.Users[0].Id);

        await using var context = db.CreateContext(tenantContext);
        var service = new PageAccessService(context, tenantContext, new RoleResolutionService(context), TimeProvider.System);

        var first = await service.ReconcileApprovedDefaultsAsync(
            SeedData.RoleId(RoleNames.TenantAdmin), new([DomainPermissions.WelcomeInvitation.View]));
        var second = await service.ReconcileApprovedDefaultsAsync(
            SeedData.RoleId(RoleNames.TenantAdmin), new([DomainPermissions.WelcomeInvitation.View]));

        Assert.True(first.Succeeded, first.Message);
        Assert.True(second.Succeeded, second.Message);
        Assert.Contains(DomainPermissions.WelcomeInvitation.View, second.Value!.GrantedPermissions);
        Assert.Empty(await context.AuthorizationConfigurationEvents.Where(x =>
            x.RoleId == SeedData.RoleId(RoleNames.TenantAdmin) &&
            x.PermissionCode == DomainPermissions.WelcomeInvitation.View &&
            x.Action == "Reconciled").ToListAsync());
    }

    [Fact]
    public async Task Approved_reconciliation_preserves_explicit_revocations_and_custom_roles()
    {
        using var db = new SqliteInMemoryDatabase();
        await db.SeedAsync();
        var tenantContext = new TestTenantContext(SeedData.TenantIds.Demo01, SeedData.Users[0].Id);
        var customRoleId = 990001;

        await using (var arrange = db.CreateContext(new TestTenantContext()))
        {
            var permissionId = SeedData.PermissionId(DomainPermissions.WelcomeInvitation.View);
            arrange.RolePermissions.Remove(await arrange.RolePermissions.SingleAsync(x => x.RoleId == SeedData.RoleId(RoleNames.TenantAdmin) && x.PermissionId == permissionId));
            arrange.Roles.Add(new Role { Id = customRoleId, Name = "Custom Welcome Role" });
            arrange.AuthorizationConfigurationEvents.Add(new AuthorizationConfigurationEvent
            {
                Id = Guid.NewGuid(), TenantId = SeedData.TenantIds.Demo01,
                EventType = AuthorizationConfigurationEventType.RolePermissionRevoked,
                EntityType = nameof(RolePermission), RoleId = SeedData.RoleId(RoleNames.TenantAdmin), PermissionId = permissionId,
                PermissionCode = DomainPermissions.WelcomeInvitation.View, Action = "Revoked", ActorUserId = SeedData.Users[0].Id,
                OccurredAtUtc = DateTime.UtcNow
            });
            await arrange.SaveChangesAsync();
        }

        await using var context = db.CreateContext(tenantContext);
        var service = new PageAccessService(context, tenantContext, new RoleResolutionService(context), TimeProvider.System);
        var revoked = await service.ReconcileApprovedDefaultsAsync(SeedData.RoleId(RoleNames.TenantAdmin), new([DomainPermissions.WelcomeInvitation.View]));
        Assert.True(revoked.Succeeded);
        Assert.DoesNotContain(DomainPermissions.WelcomeInvitation.View, revoked.Value!.GrantedPermissions);

        var custom = await service.ReconcileApprovedDefaultsAsync(customRoleId, new([DomainPermissions.WelcomeInvitation.View]));
        Assert.Equal(ResultStatus.ValidationFailed, custom.Status);
        Assert.False(await context.RolePermissions.AnyAsync(x => x.RoleId == customRoleId));
    }

    [Fact]
    public async Task Explicit_page_access_regrant_is_not_silently_reversed_by_later_reconciliation()
    {
        using var db = new SqliteInMemoryDatabase();
        await db.SeedAsync();
        var tenantContext = new TestTenantContext(SeedData.TenantIds.Demo01, SeedData.Users[0].Id);
        var roleId = SeedData.RoleId(RoleNames.TenantAdmin);
        var permissionId = SeedData.PermissionId(DomainPermissions.WelcomeInvitation.View);

        await using (var arrange = db.CreateContext(new TestTenantContext()))
        {
            arrange.RolePermissions.Remove(await arrange.RolePermissions.SingleAsync(x => x.RoleId == roleId && x.PermissionId == permissionId));
            arrange.AuthorizationConfigurationEvents.Add(new AuthorizationConfigurationEvent
            {
                Id = Guid.NewGuid(), TenantId = tenantContext.TenantId!.Value, EventType = AuthorizationConfigurationEventType.RolePermissionRevoked,
                EntityType = nameof(RolePermission), RoleId = roleId, PermissionId = permissionId,
                PermissionCode = DomainPermissions.WelcomeInvitation.View, Action = "Revoked",
                ActorUserId = tenantContext.UserId!.Value, OccurredAtUtc = DateTime.UtcNow
            });
            await arrange.SaveChangesAsync();
        }

        await using var context = db.CreateContext(tenantContext);
        var service = new PageAccessService(context, tenantContext, new RoleResolutionService(context), TimeProvider.System);

        var explicitRegrant = await service.UpdateAsync(roleId, new([DomainPermissions.WelcomeInvitation.View], "Approved re-grant"));
        Assert.True(explicitRegrant.Succeeded, explicitRegrant.Message);
        Assert.Contains(DomainPermissions.WelcomeInvitation.View, explicitRegrant.Value!.GrantedPermissions);

        var reconciled = await service.ReconcileApprovedDefaultsAsync(roleId, new([DomainPermissions.WelcomeInvitation.View]));
        Assert.True(reconciled.Succeeded, reconciled.Message);
        Assert.Contains(DomainPermissions.WelcomeInvitation.View, reconciled.Value!.GrantedPermissions);
        Assert.Empty(await context.AuthorizationConfigurationEvents.Where(x => x.RoleId == roleId && x.Action == "Reconciled").ToListAsync());
    }

    [Fact]
    public async Task Approved_reconciliation_rolls_back_permission_and_audit_event_when_save_fails()
    {
        using var db = new SqliteInMemoryDatabase();
        await db.SeedAsync();
        var tenantContext = new TestTenantContext(SeedData.TenantIds.Demo01, SeedData.Users[0].Id);
        var roleId = SeedData.RoleId(RoleNames.TenantAdmin);
        var permissionId = SeedData.PermissionId(DomainPermissions.WelcomeInvitation.View);

        await using (var arrange = db.CreateContext(new TestTenantContext()))
        {
            arrange.RolePermissions.Remove(await arrange.RolePermissions.SingleAsync(x => x.RoleId == roleId && x.PermissionId == permissionId));
            await arrange.SaveChangesAsync();
        }

        await using (var failing = db.CreateContext(tenantContext, new ThrowOnAddedReconciliationSave()))
        {
            var service = new PageAccessService(failing, tenantContext, new RoleResolutionService(failing), TimeProvider.System);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReconcileApprovedDefaultsAsync(roleId, new([DomainPermissions.WelcomeInvitation.View])));
        }

        await using var verify = db.CreateContext(tenantContext);
        Assert.False(await verify.RolePermissions.AnyAsync(x => x.RoleId == roleId && x.PermissionId == permissionId));
        Assert.Empty(await verify.AuthorizationConfigurationEvents.Where(x => x.RoleId == roleId && x.PermissionId == permissionId && x.Action == "Reconciled").ToListAsync());
    }

    private sealed class ThrowOnAddedReconciliationSave : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<AuthorizationConfigurationEvent>().Any(x => x.State == EntityState.Added && x.Entity.Action == "Reconciled") == true)
                throw new InvalidOperationException("Synthetic authorization audit failure.");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    [Fact]
    public async Task Seed_populates_an_organization_structure_for_each_tenant()
    {
        using var db = new SqliteInMemoryDatabase();
        await db.SeedAsync();

        using var context = db.CreateContext(new TestTenantContext());

        Assert.Equal(SeedData.Departments.Count, await context.Departments.IgnoreQueryFilters().CountAsync());
        Assert.Equal(SeedData.Designations.Count, await context.Designations.IgnoreQueryFilters().CountAsync());
        Assert.Equal(SeedData.Employees.Count, await context.Employees.IgnoreQueryFilters().CountAsync());

        // Deliberately different sizes, so a leak between the two shows up as a wrong number rather than as
        // two tenants that happen to look alike.
        Assert.Equal(3, await context.Departments.IgnoreQueryFilters()
            .CountAsync(d => d.TenantId == SeedData.TenantIds.Demo01));
        Assert.Equal(2, await context.Departments.IgnoreQueryFilters()
            .CountAsync(d => d.TenantId == SeedData.TenantIds.Demo02));
        Assert.Equal(6, await context.Employees.IgnoreQueryFilters()
            .CountAsync(e => e.TenantId == SeedData.TenantIds.Demo01));
        Assert.Equal(2, await context.Employees.IgnoreQueryFilters()
            .CountAsync(e => e.TenantId == SeedData.TenantIds.Demo02));
    }

    /// <summary>
    /// The seed is the one place that writes employees without going through the service, so it is also the
    /// one place that could quietly create a cross-tenant reference — which every later isolation test would
    /// then inherit as its starting state.
    /// </summary>
    [Fact]
    public async Task Every_seeded_employee_references_only_its_own_tenants_rows()
    {
        using var db = new SqliteInMemoryDatabase();
        await db.SeedAsync();

        using var context = db.CreateContext(new TestTenantContext());
        var employees = await context.Employees
            .IgnoreQueryFilters()
            .Include(e => e.Department)
            .Include(e => e.Designation)
            .Include(e => e.ReportingManager)
            .ToListAsync();

        Assert.Equal(SeedData.Employees.Count, employees.Count);

        foreach (var employee in employees)
        {
            Assert.Equal(employee.TenantId, employee.Department!.TenantId);
            Assert.Equal(employee.TenantId, employee.Designation!.TenantId);

            if (employee.ReportingManager is not null)
            {
                Assert.Equal(employee.TenantId, employee.ReportingManager.TenantId);
            }
        }
    }

    [Fact]
    public async Task Seeded_reporting_lines_are_wired_by_employee_code()
    {
        using var db = new SqliteInMemoryDatabase();
        await db.SeedAsync();

        using var context = db.CreateContext(new TestTenantContext());
        var employees = await context.Employees
            .IgnoreQueryFilters()
            .Include(e => e.ReportingManager)
            .ToDictionaryAsync(e => $"{e.TenantId}/{e.EmployeeCode}");

        var demo01 = SeedData.TenantIds.Demo01;
        var demo02 = SeedData.TenantIds.Demo02;

        Assert.Null(employees[$"{demo01}/EMP-001"].ReportingManagerId);
        Assert.Equal("EMP-001", employees[$"{demo01}/EMP-002"].ReportingManager!.EmployeeCode);
        Assert.Equal("EMP-002", employees[$"{demo01}/EMP-003"].ReportingManager!.EmployeeCode);
        Assert.Null(employees[$"{demo02}/E-100"].ReportingManagerId);
        Assert.Equal("E-100", employees[$"{demo02}/E-101"].ReportingManager!.EmployeeCode);

        // Nobody reports to somebody in another organization.
        Assert.All(employees.Values, e => Assert.True(
            e.ReportingManager is null || e.ReportingManager.TenantId == e.TenantId));
    }

    [Fact]
    public async Task Seeded_admin_belongs_to_its_tenant_and_password_verifies()
    {
        using var db = new SqliteInMemoryDatabase();
        await db.SeedAsync();

        using var context = db.CreateContext(new TestTenantContext());
        var admin = await context.Users.IgnoreQueryFilters()
            .SingleAsync(u => u.Email == "admin@demo01.com");

        Assert.Equal(SeedData.TenantIds.Demo01, admin.TenantId);

        var hasher = new IdentityPasswordHasher();
        Assert.True(hasher.Verify(admin.PasswordHash, SeedData.DefaultUserPassword));
        Assert.False(hasher.Verify(admin.PasswordHash, "not-the-password"));
    }

    [Fact]
    public async Task Role_permission_grants_match_the_seed_map()
    {
        using var db = new SqliteInMemoryDatabase();
        await db.SeedAsync();

        using var context = db.CreateContext(new TestTenantContext());

        var tenantAdminGrants = await context.RolePermissions
            .CountAsync(rp => rp.RoleId == SeedData.RoleId(RoleNames.TenantAdmin));
        Assert.Equal(SeedData.RolePermissionMap[RoleNames.TenantAdmin].Length, tenantAdminGrants);

        var employeeGrants = await context.RolePermissions
            .CountAsync(rp => rp.RoleId == SeedData.RoleId(RoleNames.Employee));
        Assert.Equal(SeedData.RolePermissionMap[RoleNames.Employee].Length, employeeGrants);
        Assert.Equal(new[]
        {
            DomainPermissions.Geography.View,
            DomainPermissions.Attendance.MonthlyViewSelf,
            DomainPermissions.Attendance.ExceptionViewSelf,
            DomainPermissions.Attendance.CompOffViewSelf,
            DomainPermissions.Leave.RequestCreate,
            DomainPermissions.Leave.RequestViewOwn,
            DomainPermissions.Leave.RequestWithdrawOwn,
            DomainPermissions.Leave.RequestCancelOwn,
            DomainPermissions.Leave.BalanceViewOwn,
            DomainPermissions.Leave.TypeViewAvailable,
            DomainPermissions.Payroll.AdjustmentsView,
            DomainPermissions.Payroll.AdjustmentsViewHistory,
            DomainPermissions.Payroll.TaxDeclarationViewOwn,
            DomainPermissions.Payroll.TaxDeclarationManageOwn,
            DomainPermissions.Separation.ViewSelf,
            DomainPermissions.Separation.CreateSelf,
            DomainPermissions.Separation.Submit,
            DomainPermissions.Separation.Withdraw,
            DomainPermissions.Separation.ViewHistory
        }, SeedData.RolePermissionMap[RoleNames.Employee]);
        Assert.DoesNotContain(DomainPermissions.Leave.PolicyView, SeedData.RolePermissionMap[RoleNames.Employee]);
        Assert.DoesNotContain(DomainPermissions.Leave.Approve, SeedData.RolePermissionMap[RoleNames.Employee]);
        Assert.Contains(DomainPermissions.Leave.Approve, SeedData.RolePermissionMap[RoleNames.Manager]);
        var managerGrants = await context.RolePermissions
            .CountAsync(rp => rp.RoleId == SeedData.RoleId(RoleNames.Manager));
        Assert.Equal(SeedData.RolePermissionMap[RoleNames.Manager].Length, managerGrants);
        Assert.Equal(62, SeedData.PermissionId(DomainPermissions.Leave.RequestCreate));
        Assert.Equal(67, SeedData.PermissionId(DomainPermissions.Leave.TypeViewAvailable));
    }

    /// <summary>
    /// The seeded branding is read by anonymous callers, so what it holds is a disclosure decision rather
    /// than sample data. It is asserted against the catalog, which is where it lives — it deliberately left
    /// the tenant databases, since it has to be readable before one has been chosen.
    /// </summary>
    [Fact]
    public async Task Seed_gives_each_demo_organization_public_branding_with_its_own_accent()
    {
        using var db = new SqliteInMemoryDatabase();
        await db.SeedAsync();

        using var catalog = db.CreateCatalogContext();
        var branding = await catalog.TenantBranding.AsNoTracking().ToDictionaryAsync(b => b.TenantId);

        Assert.Equal(SeedData.Branding.Count, branding.Count);
        Assert.Equal(await catalog.Tenants.CountAsync(), branding.Count);

        var demo01 = branding[SeedData.TenantIds.Demo01];
        var demo02 = branding[SeedData.TenantIds.Demo02];

        // Opted in, or the development login screen would show the neutral response and the feature would
        // look broken rather than closed.
        Assert.True(demo01.IsPublic);
        Assert.True(demo02.IsPublic);

        // Different accents, so which organization's branding resolved is visible without reading the DOM.
        Assert.Equal("#0F766E", demo01.PrimaryColor);
        Assert.Equal("#7C3AED", demo02.PrimaryColor);

        // No logo, because a seeded URL would be a dead link or a dependency on someone else's host; and no
        // single sign-on, because no provider is implemented and advertising one would be a lie in the UI.
        Assert.All(branding.Values, b =>
        {
            Assert.Null(b.LogoUrl);
            Assert.False(b.SsoEnabled);
            Assert.Null(b.SsoProviderName);
        });
    }

    /// <summary>
    /// Branding is admin-editable, so a restart must not put the seed values back over someone's choices —
    /// including a choice to stop publishing. An unconditional upsert would silently re-open that.
    /// </summary>
    [Fact]
    public async Task Reseeding_leaves_edited_branding_alone()
    {
        using var db = new SqliteInMemoryDatabase();
        await db.SeedAsync();

        using (var catalog = db.CreateCatalogContext())
        {
            var branding = await catalog.TenantBranding.SingleAsync(b => b.TenantId == SeedData.TenantIds.Demo01);
            branding.IsPublic = false;
            branding.DisplayName = "Renamed By An Administrator";
            await catalog.SaveChangesAsync();
        }

        await db.SeedAsync();

        using var verification = db.CreateCatalogContext();
        var reread = await verification.TenantBranding.AsNoTracking()
            .SingleAsync(b => b.TenantId == SeedData.TenantIds.Demo01);

        Assert.False(reread.IsPublic);
        Assert.Equal("Renamed By An Administrator", reread.DisplayName);
        Assert.Equal(SeedData.Branding.Count, await verification.TenantBranding.CountAsync());
    }

    /// <summary>
    /// Reference-row ids are hand-assigned, replicated into every tenant database, and never regenerated —
    /// so a missing or duplicated one is not a test-time inconvenience, it is data corruption spread across
    /// every customer's database by the next startup. These three tests are the guard that used to be
    /// unnecessary when the ids were list positions, and that became necessary the moment they were not:
    /// nothing else notices a role added to <see cref="RoleNames.All"/> without an id, or two names given the
    /// same number.
    /// </summary>
    [Fact]
    public void Every_role_and_permission_has_a_seeded_id()
    {
        Assert.All(RoleNames.All, name => Assert.True(SeedData.RoleId(name) > 0));
        Assert.All(DomainPermissions.All, name => Assert.True(SeedData.PermissionId(name) > 0));
    }

    [Fact]
    public void Seeded_ids_are_unique_within_roles_and_within_permissions()
    {
        var roleIds = RoleNames.All.Select(SeedData.RoleId).ToList();
        Assert.Equal(roleIds.Count, roleIds.Distinct().Count());

        var permissionIds = DomainPermissions.All.Select(SeedData.PermissionId).ToList();
        Assert.Equal(permissionIds.Count, permissionIds.Distinct().Count());
    }

    /// <summary>
    /// An unmapped name has to throw rather than fall back to zero or to a position: a grant written against
    /// id 0 would insert cleanly and grant nothing, which is the failure mode hardest to notice.
    /// </summary>
    [Fact]
    public void An_unmapped_role_or_permission_name_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SeedData.RoleId("NoSuchRole"));
        Assert.Throws<ArgumentOutOfRangeException>(() => SeedData.PermissionId("nosuch.permission"));
    }

    /// <summary>
    /// The rows that get inserted must carry the same ids the grants are written against. They come from two
    /// different code paths — the entity lists and <see cref="SeedData.RoleId"/> — and agreeing today is not
    /// the same as agreeing after an edit.
    /// </summary>
    [Fact]
    public void Seeded_rows_carry_the_ids_the_lookup_reports()
    {
        Assert.All(SeedData.Roles, role => Assert.Equal(SeedData.RoleId(role.Name), role.Id));
        Assert.All(SeedData.Permissions, p => Assert.Equal(SeedData.PermissionId(p.Name), p.Id));
    }

    [Fact]
    public async Task Development_verification_password_reset_is_never_applied_outside_development()
    {
        using var db = new SqliteInMemoryDatabase();
        var tenant = await EnsureVerificationTenantAsync(db);
        await SeedVerificationAsync(db, tenant, VerificationPassword, reset: false, isDevelopment: true);

        await SeedVerificationAsync(db, tenant, ResetPassword, reset: true, isDevelopment: false);

        using var context = db.CreateContext(new TestTenantContext());
        var user = await context.Users.IgnoreQueryFilters().SingleAsync(x => x.Email == VerificationEmail);
        var hasher = new IdentityPasswordHasher();
        Assert.True(hasher.Verify(user.PasswordHash, VerificationPassword));
        Assert.False(hasher.Verify(user.PasswordHash, ResetPassword));
    }

    [Fact]
    public async Task Development_verification_password_is_unchanged_without_reset_flag()
    {
        using var db = new SqliteInMemoryDatabase();
        var tenant = await EnsureVerificationTenantAsync(db);
        await SeedVerificationAsync(db, tenant, VerificationPassword, reset: false, isDevelopment: true);

        await SeedVerificationAsync(db, tenant, ResetPassword, reset: false, isDevelopment: true);

        using var context = db.CreateContext(new TestTenantContext());
        var user = await context.Users.IgnoreQueryFilters().SingleAsync(x => x.Email == VerificationEmail);
        var hasher = new IdentityPasswordHasher();
        Assert.True(hasher.Verify(user.PasswordHash, VerificationPassword));
        Assert.False(hasher.Verify(user.PasswordHash, ResetPassword));
    }

    [Fact]
    public async Task Development_reset_updates_only_the_fixed_verification_account()
    {
        using var db = new SqliteInMemoryDatabase();
        var tenant = await EnsureVerificationTenantAsync(db);
        await SeedVerificationAsync(db, tenant, VerificationPassword, reset: false, isDevelopment: true);

        using (var context = db.CreateContext(new TestTenantContext()))
        {
            context.Users.Add(new User
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.Id,
                Email = "unrelated-development-user@local.invalid",
                FirstName = "Unrelated",
                LastName = "User",
                PasswordHash = new IdentityPasswordHasher().Hash(VerificationPassword)
            });
            await context.SaveChangesAsync();
        }

        await SeedVerificationAsync(db, tenant, ResetPassword, reset: true, isDevelopment: true);

        using var verification = db.CreateContext(new TestTenantContext());
        var users = await verification.Users.IgnoreQueryFilters()
            .Where(x => x.Email == VerificationEmail || x.Email == "unrelated-development-user@local.invalid")
            .ToDictionaryAsync(x => x.Email);
        var hasher = new IdentityPasswordHasher();
        Assert.True(hasher.Verify(users[VerificationEmail].PasswordHash, ResetPassword));
        Assert.True(hasher.Verify(users["unrelated-development-user@local.invalid"].PasswordHash, VerificationPassword));
    }

    [Fact]
    public async Task Development_reset_without_password_does_not_change_password_and_logs_safe_warning()
    {
        using var db = new SqliteInMemoryDatabase();
        var tenant = await EnsureVerificationTenantAsync(db);
        await SeedVerificationAsync(db, tenant, VerificationPassword, reset: false, isDevelopment: true);
        var loggerProvider = new RecordingLoggerProvider();

        await SeedVerificationAsync(db, tenant, password: null, reset: true, isDevelopment: true, loggerProvider);

        using var context = db.CreateContext(new TestTenantContext());
        var user = await context.Users.IgnoreQueryFilters().SingleAsync(x => x.Email == VerificationEmail);
        Assert.True(new IdentityPasswordHasher().Verify(user.PasswordHash, VerificationPassword));
        var warning = Assert.Single(loggerProvider.Messages, message => message.Contains("no password", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(VerificationPassword, warning, StringComparison.Ordinal);
        Assert.DoesNotContain(ResetPassword, warning, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Development_verification_admin_eligibility_is_not_added_outside_development()
    {
        using var db = new SqliteInMemoryDatabase();
        var tenant = await EnsureVerificationTenantAsync(db);
        await SeedVerificationAsync(db, tenant, VerificationPassword, reset: false, isDevelopment: true);
        await SeedVerificationAsync(db, tenant, VerificationPassword, reset: false, isDevelopment: false, enableAdmin: true);

        using var context = db.CreateContext(new TestTenantContext());
        var user = await context.Users.IgnoreQueryFilters().SingleAsync(x => x.Email == VerificationEmail);
        Assert.Empty(await context.UserRoles.IgnoreQueryFilters().Where(x => x.UserId == user.Id && x.RoleId == SeedData.RoleId(RoleNames.TenantAdmin)).ToListAsync());
    }

    [Fact]
    public async Task Development_exact_Anevra_verification_account_gets_existing_tenant_admin_eligibility()
    {
        using var db = new SqliteInMemoryDatabase();
        var tenant = await EnsureVerificationTenantAsync(db);
        await SeedVerificationAsync(db, tenant, VerificationPassword, reset: false, isDevelopment: true, enableAdmin: true);

        using var context = db.CreateContext(new TestTenantContext());
        var user = await context.Users.IgnoreQueryFilters().SingleAsync(x => x.Email == VerificationEmail);
        Assert.Single(await context.UserRoles.IgnoreQueryFilters().Where(x => x.UserId == user.Id && x.RoleId == SeedData.RoleId(RoleNames.TenantAdmin)).ToListAsync());
    }

    [Fact]
    public async Task Development_admin_eligibility_does_not_affect_another_account_and_is_idempotent()
    {
        using var db = new SqliteInMemoryDatabase();
        var tenant = await EnsureVerificationTenantAsync(db);
        var unrelatedId = Guid.NewGuid();
        using (var context = db.CreateContext(new TestTenantContext()))
        {
            context.Users.Add(new User { Id = unrelatedId, TenantId = tenant.Id, Email = "unrelated@local.invalid", FirstName = "Unrelated", LastName = "User", PasswordHash = new IdentityPasswordHasher().Hash(VerificationPassword), IsActive = true });
            await context.SaveChangesAsync();
        }

        await SeedVerificationAsync(db, tenant, VerificationPassword, reset: false, isDevelopment: true, enableAdmin: true);
        await SeedVerificationAsync(db, tenant, VerificationPassword, reset: false, isDevelopment: true, enableAdmin: true);

        using var verification = db.CreateContext(new TestTenantContext());
        var user = await verification.Users.IgnoreQueryFilters().SingleAsync(x => x.Email == VerificationEmail);
        Assert.Single(await verification.UserRoles.IgnoreQueryFilters().Where(x => x.UserId == user.Id && x.RoleId == SeedData.RoleId(RoleNames.TenantAdmin)).ToListAsync());
        Assert.Empty(await verification.UserRoles.IgnoreQueryFilters().Where(x => x.UserId == unrelatedId && x.RoleId == SeedData.RoleId(RoleNames.TenantAdmin)).ToListAsync());
        Assert.Single(await verification.UserRoleAssignmentEvents.IgnoreQueryFilters().Where(x => x.UserId == user.Id && x.RoleId == SeedData.RoleId(RoleNames.TenantAdmin)).ToListAsync());
    }

    private const string VerificationEmail = "anevra01-role-verification@local.invalid";

    private static async Task<Tenant> EnsureVerificationTenantAsync(SqliteInMemoryDatabase db)
    {
        var tenant = new Tenant
        {
            Id = new Guid("f92b6b9a-512a-42b2-b653-907cc2c1f70e"),
            TenantCode = "ANEVRA01",
            Host = "anevra01.localhost",
            ShardKey = "anevra01",
            DatabaseProvider = DatabaseProviderType.MySql,
            TenantName = "ANEVRA01",
            Status = TenantStatus.Active
        };
        using var context = db.CreateContext(new TestTenantContext());
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();
        return tenant;
    }

    private static async Task SeedVerificationAsync(
        SqliteInMemoryDatabase db,
        Tenant tenant,
        string? password,
        bool reset,
        bool isDevelopment,
        RecordingLoggerProvider? loggerProvider = null,
        bool enableAdmin = false)
    {
        var values = new Dictionary<string, string?>
        {
            ["DevelopmentSeed:AnevraAdminPassword"] = password,
            ["DevelopmentSeed:ResetAnevraVerificationPassword"] = reset.ToString(),
            ["DevelopmentSeed:EnableAnevraVerificationAdmin"] = enableAdmin.ToString()
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        using var context = db.CreateContext(new TestTenantContext());
        await DatabaseSeeder.SeedShardAsync(
            context,
            new IdentityPasswordHasher(),
            tenant,
            CancellationToken.None,
            loggerProvider?.CreateLogger("seed") ?? NullLogger.Instance,
            configuration,
            isDevelopment);
    }

    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        public ConcurrentBag<string> Messages { get; } = new();

        public ILogger CreateLogger(string categoryName) => new RecordingLogger(Messages);

        public void Dispose() { }
    }

    private sealed class RecordingLogger(ConcurrentBag<string> messages) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullLogger.Instance.BeginScope(state);
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => messages.Add(formatter(state, exception));
    }
}
