using HRMS.Application.Common;
using HRMS.Application.DTOs.AccountEmployeeLinks;
using HRMS.Application.Services;
using HRMS.Domain.Authorization;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

/// <summary>Focused Phase 3B tests over disposable SQLite with foreign keys enabled.</summary>
public sealed class AccountEmployeeLinkServiceTests
{
    private static readonly Guid Tenant = SeedData.TenantIds.Demo01;
    private static readonly Guid OtherTenant = SeedData.TenantIds.Demo02;
    private static readonly Guid Actor = SeedData.Users[0].Id;
    private static readonly Guid Subject = SeedData.Users[1].Id;
    private static readonly Guid OtherSubject = new("a3333333-1111-1111-1111-111111111111");
    private static readonly Guid ThirdSubject = new("a4444444-1111-1111-1111-111111111111");
    private static readonly Guid Employee = new("13000000-0000-0000-0000-000000000001");
    private static readonly Guid OtherEmployee = OrganizationTestHarness.EmployeeId(OtherTenant, "E-100");
    private static readonly Guid ReplacementEmployee = new("13000000-0000-0000-0000-000000000002");

    [Fact]
    public async Task Link_unlink_and_relink_append_events_and_require_the_current_revision()
    {
        using var h = await ReadyAsync();
        var service = Service(h);

        var linked = await service.LinkAsync(Subject, new(Employee, null, "initial link"));
        Assert.True(linked.Succeeded, linked.Message);
        var link = linked.Value!.CurrentLink!;
        Assert.Equal(link.LinkId, linked.Value.Revision);

        var secondLink = await service.LinkAsync(Subject,
            new(ReplacementEmployee, linked.Value.Revision, "duplicate account"));
        Assert.Equal(ResultStatus.Conflict, secondLink.Status);

        var staleUnlink = await service.UnlinkAsync(Subject,
            new(link.LinkId, Employee, Guid.NewGuid(), "stale"));
        Assert.Equal(ResultStatus.Conflict, staleUnlink.Status);

        var unlinked = await service.UnlinkAsync(Subject,
            new(link.LinkId, Employee, linked.Value.Revision, "cleanup"));
        Assert.True(unlinked.Succeeded, unlinked.Message);
        Assert.Equal("Unlinked", unlinked.Value!.Status);

        var relinked = await service.LinkAsync(Subject,
            new(ReplacementEmployee, unlinked.Value.Revision, "relink"));
        Assert.True(relinked.Succeeded, relinked.Message);

        var history = await service.GetHistoryAsync(Subject, new(1, 10));
        Assert.True(history.Succeeded, history.Message);
        Assert.Equal(new[] { "Link", "Unlink", "Link" }, history.Value!.Items.Reverse().Select(x => x.Operation));
        Assert.Equal(new[] { 1L, 2L, 3L }, history.Value.Items.Reverse().Select(x => x.Sequence));
    }

    [Fact]
    public async Task Occupied_targets_self_links_cross_tenant_targets_and_disabled_subjects_are_rejected()
    {
        using var h = await ReadyAsync();
        var service = Service(h);
        var first = await service.LinkAsync(Subject, new(Employee, null, "first"));
        Assert.True(first.Succeeded, first.Message);

        var occupied = await service.LinkAsync(OtherSubject,
            new(Employee, null, "occupied"));
        Assert.Equal(ResultStatus.Conflict, occupied.Status);

        var self = await service.LinkAsync(Actor, new(Employee, null, "self"));
        Assert.Equal(ResultStatus.Forbidden, self.Status);

        var crossTenant = await service.LinkAsync(new("a4444444-1111-1111-1111-111111111111"),
            new(OtherEmployee, null, "cross tenant"));
        Assert.Equal(ResultStatus.NotFound, crossTenant.Status);

        using (var context = h.CreateContext())
        {
            var subject = await context.Users.SingleAsync(x => x.Id == Subject);
            subject.IsActive = false;
            await context.SaveChangesAsync();
        }

        var disabledCleanup = await service.UnlinkAsync(Subject,
            new(first.Value!.CurrentLink!.LinkId, Employee, first.Value.Revision, "disabled cleanup"));
        Assert.True(disabledCleanup.Succeeded, disabledCleanup.Message);
    }

    [Fact]
    public async Task Replacement_is_atomic_rejects_occupied_targets_and_preserves_the_original_link()
    {
        using var h = await ReadyAsync();
        var service = Service(h);
        var original = await service.LinkAsync(Subject, new(Employee, null, "original"));
        Assert.True(original.Succeeded, original.Message);

        var other = await service.LinkAsync(OtherSubject,
            new(ReplacementEmployee, null, "other"));
        Assert.True(other.Succeeded, other.Message);

        var rejected = await service.ReplaceAsync(Subject, new(
            original.Value!.CurrentLink!.LinkId, Employee, original.Value.Revision,
            other.Value!.CurrentLink!.EmployeeId, "occupied target"));
        Assert.Equal(ResultStatus.Conflict, rejected.Status);

        var current = await service.GetUserAsync(Subject);
        Assert.Equal(Employee, current.Value!.CurrentLink!.EmployeeId);
        Assert.Equal(1, (await service.GetHistoryAsync(Subject, new(1, 10))).Value!.TotalCount);
    }

    [Fact]
    public async Task Live_grants_are_separate_and_revocation_is_seen_without_a_new_token()
    {
        using var h = await ReadyAsync(includeManage: false);
        var service = Service(h);
        Assert.Equal(ResultStatus.Forbidden,
            (await service.LinkAsync(Subject, new(Employee, null, "not granted"))).Status);

        await GrantManageAsync(h);
        var linked = await service.LinkAsync(Subject, new(Employee, null, "granted"));
        Assert.True(linked.Succeeded, linked.Message);

        await RevokeManageAsync(h);
        Assert.Equal(ResultStatus.Forbidden,
            (await service.UnlinkAsync(Subject, new(
                linked.Value!.CurrentLink!.LinkId, Employee, linked.Value.Revision, "revoked"))).Status);
    }

    [Fact]
    public async Task View_history_and_manage_are_independent_and_broad_admin_roles_do_not_gain_link_grants()
    {
        using var h = await ReadyAsync(includeManage: false);
        using (var context = h.CreateContext())
        {
            var roleId = SeedData.RoleId(RoleNames.AccountLinkAuditor);
            context.UserRoles.Add(new UserRole { UserId = Actor, RoleId = roleId, TenantId = Tenant });
            context.RolePermissions.Add(new RolePermission
            {
                RoleId = roleId,
                PermissionId = SeedData.PermissionId(Permissions.AccountEmployeeLink.View)
            });
            context.RolePermissions.Add(new RolePermission
            {
                RoleId = roleId,
                PermissionId = SeedData.PermissionId(Permissions.AccountEmployeeLink.ViewHistory)
            });
            await context.SaveChangesAsync();
        }

        Assert.DoesNotContain(Permissions.AccountEmployeeLink.View, SeedData.RolePermissionMap[RoleNames.TenantAdmin]);
        Assert.Contains(Permissions.AccountEmployeeLink.ViewHistory, Permissions.All);
        Assert.Equal(ResultStatus.Success, (await Service(h).GetUserAsync(Subject)).Status);
        Assert.Equal(ResultStatus.Success,
            (await Service(h).GetHistoryAsync(Subject, new(1, 10))).Status);
        Assert.Equal(ResultStatus.Forbidden,
            (await Service(h).LinkAsync(Subject, new(Employee, null, "auditor cannot mutate"))).Status);
    }

    [Fact]
    public async Task Linked_accounts_remain_selectable_while_linked_employees_leave_new_link_candidates()
    {
        using var h = await ReadyAsync();
        var service = Service(h);
        var linked = await service.LinkAsync(Subject, new(Employee, null, "candidate coverage"));
        Assert.True(linked.Succeeded, linked.Message);

        var users = await service.GetUserCandidatesAsync(new(1, 50));
        Assert.True(users.Succeeded, users.Message);
        Assert.Contains(users.Value!.Items, x => x.Id == Subject);

        var employees = await service.GetEmployeeCandidatesAsync(new(1, 50));
        Assert.True(employees.Succeeded, employees.Message);
        Assert.DoesNotContain(employees.Value!.Items, x => x.Id == Employee);
    }

    [Fact]
    public async Task Historical_identity_references_are_immutable_and_block_deletion_after_unlink()
    {
        using var h = await ReadyAsync();
        var service = Service(h);
        var linked = await service.LinkAsync(Subject, new(Employee, null, "retain history"));
        Assert.True(linked.Succeeded, linked.Message);
        var unlinked = await service.UnlinkAsync(Subject, new(
            linked.Value!.CurrentLink!.LinkId, Employee, linked.Value.Revision, "retain history"));
        Assert.True(unlinked.Succeeded, unlinked.Message);

        using var context = h.CreateUnscopedContext();
        var evt = await context.AccountEmployeeLinkEvents.IgnoreQueryFilters().SingleAsync(x => x.SubjectUserId == Subject && x.Operation == "Link");
        evt.Reason = "tampered";
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        context.ChangeTracker.Clear();
        context.Users.Remove(await context.Users.IgnoreQueryFilters().SingleAsync(x => x.Id == Subject));
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        context.ChangeTracker.Clear();
        context.Employees.Remove(await context.Employees.IgnoreQueryFilters().SingleAsync(x => x.Id == Employee));
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Audit_failure_rolls_back_the_event_and_current_state_together()
    {
        using var h = await ReadyAsync();
        var service = new AccountEmployeeLinkService(
            h.CreateContext(), h.TenantContext, h.Clock,
            () => throw new InvalidOperationException("synthetic audit failure"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.LinkAsync(Subject, new(Employee, null, "rollback")));

        using var read = h.CreateUnscopedContext();
        Assert.Empty(await read.AccountEmployeeLinkEvents.IgnoreQueryFilters()
            .Where(x => x.SubjectUserId == Subject).ToListAsync());
        Assert.Empty(await read.AccountEmployeeCurrentLinks.IgnoreQueryFilters()
            .Where(x => x.UserId == Subject).ToListAsync());
    }

    private static AccountEmployeeLinkService Service(OrganizationTestHarness h) =>
        new(h.CreateContext(), h.TenantContext, h.Clock);

    private static async Task<OrganizationTestHarness> ReadyAsync(bool includeManage = true)
    {
        var h = await OrganizationTestHarness.CreateAsync();
        h.TenantContext.UserId = Actor;
        using (var context = h.CreateContext())
        {
            context.Users.Add(new User
            {
                Id = OtherSubject, TenantId = Tenant, Email = "other@demo01.test",
                FirstName = "Other", LastName = "Subject", PasswordHash = "test", IsActive = true
            });
            context.Users.Add(new User
            {
                Id = ThirdSubject, TenantId = Tenant, Email = "third@demo01.test",
                FirstName = "Third", LastName = "Subject", PasswordHash = "test", IsActive = true
            });
            context.Employees.Add(new Employee
            {
                Id = Employee, TenantId = Tenant, Email = "future1@demo01.test",
                FirstName = "Future", LastName = "Joiner", DateOfJoining = new DateOnly(2030, 1, 1),
                Status = EmployeeStatus.Active
            });
            context.Employees.Add(new Employee
            {
                Id = ReplacementEmployee, TenantId = Tenant, Email = "future2@demo01.test",
                FirstName = "Second", LastName = "Joiner", DateOfJoining = new DateOnly(2030, 1, 1),
                Status = EmployeeStatus.Active
            });
            await context.SaveChangesAsync();
        }
        await GrantManageAsync(h, includeManage);
        return h;
    }

    private static async Task GrantManageAsync(OrganizationTestHarness h, bool includeManage = true)
    {
        using var context = h.CreateContext();
        var roleId = SeedData.RoleId(RoleNames.AccountLinkAdministrator);
        if (!await context.UserRoles.AnyAsync(x => x.UserId == Actor && x.RoleId == roleId))
            context.UserRoles.Add(new UserRole { UserId = Actor, RoleId = roleId, TenantId = Tenant });
        if (includeManage && !await context.RolePermissions.AnyAsync(x => x.RoleId == roleId && x.PermissionId == SeedData.PermissionId(Permissions.AccountEmployeeLink.Manage)))
            context.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionId = SeedData.PermissionId(Permissions.AccountEmployeeLink.Manage) });
        if (includeManage && !await context.RolePermissions.AnyAsync(x => x.RoleId == roleId && x.PermissionId == SeedData.PermissionId(Permissions.AccountEmployeeLink.View)))
            context.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionId = SeedData.PermissionId(Permissions.AccountEmployeeLink.View) });
        if (includeManage && !await context.RolePermissions.AnyAsync(x => x.RoleId == roleId && x.PermissionId == SeedData.PermissionId(Permissions.AccountEmployeeLink.ViewHistory)))
            context.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionId = SeedData.PermissionId(Permissions.AccountEmployeeLink.ViewHistory) });
        await context.SaveChangesAsync();
    }

    private static async Task RevokeManageAsync(OrganizationTestHarness h)
    {
        using var context = h.CreateContext();
        var roleId = SeedData.RoleId(RoleNames.AccountLinkAdministrator);
        var permissionId = SeedData.PermissionId(Permissions.AccountEmployeeLink.Manage);
        context.RolePermissions.Remove(await context.RolePermissions.SingleAsync(x => x.RoleId == roleId && x.PermissionId == permissionId));
        await context.SaveChangesAsync();
    }
}
