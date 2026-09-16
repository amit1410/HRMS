using HRMS.Application.DTOs.Roles;
using HRMS.Application.Common;
using HRMS.Application.Services;
using HRMS.Domain.Authorization;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class RoleAssignmentServiceTests
{
    private static readonly Guid Tenant = SeedData.TenantIds.Demo01;
    private static readonly Guid Actor = SeedData.Users[0].Id;
    private static readonly Guid Target = SeedData.Users[1].Id;
    private static readonly DateOnly Start = new(2027, 1, 1);

    [Fact]
    public async Task Manual_assignment_persists_explicit_dates_actor_reason_and_event()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        harness.TenantContext.UserId = Actor;
        var service = new RoleAssignmentService(harness.CreateContext(), harness.TenantContext, harness.Clock);

        var result = await service.AssignAsync(Target, new(
            SeedData.RoleId(RoleNames.HRBP), Start, new DateOnly(2027, 6, 30), "Assigned as HRBP", null));

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(Start, result.Value!.EffectiveFrom);
        Assert.Equal(new DateOnly(2027, 6, 30), result.Value.EffectiveTo);
        Assert.Equal(RoleAssignmentSource.Manual, result.Value.AssignmentSource);
        Assert.Equal(Actor, result.Value.AssignedByUserId);

        using var context = harness.CreateUnscopedContext();
        var assignment = await context.UserRoles.IgnoreQueryFilters().SingleAsync(x => x.Id == result.Value.AssignmentId);
        Assert.Equal(Tenant, assignment.TenantId);
        Assert.Equal("Assigned as HRBP", assignment.AssignmentReason);
        var audit = await context.UserRoleAssignmentEvents.IgnoreQueryFilters().SingleAsync(x => x.AssignmentId == assignment.Id);
        Assert.Equal(UserRoleAssignmentEventType.Assigned, audit.EventType);
    }

    [Fact]
    public async Task User_can_hold_multiple_manual_roles_without_overwriting_assignments()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        harness.TenantContext.UserId = Actor;
        var service = new RoleAssignmentService(harness.CreateContext(), harness.TenantContext, harness.Clock);

        var hrbp = await service.AssignAsync(Target, new(SeedData.RoleId(RoleNames.HRBP), Start, null, null, null));
        var it = await service.AssignAsync(Target, new(SeedData.RoleId(RoleNames.IT), Start, null, null, null));

        Assert.True(hrbp.Succeeded, hrbp.Message);
        Assert.True(it.Succeeded, it.Message);
        using var context = harness.CreateUnscopedContext();
        var roleIds = new[] { SeedData.RoleId(RoleNames.HRBP), SeedData.RoleId(RoleNames.IT) };
        Assert.Equal(2, await context.UserRoles.IgnoreQueryFilters().CountAsync(x => x.UserId == Target && roleIds.Contains(x.RoleId)));
    }

    [Fact]
    public async Task System_roles_cannot_be_manually_assigned_or_revoked()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        harness.TenantContext.UserId = Actor;
        var service = new RoleAssignmentService(harness.CreateContext(), harness.TenantContext, harness.Clock);

        var employee = await service.AssignAsync(Target, new(SeedData.RoleId(RoleNames.Employee), Start, null, null, null));
        var manager = await service.AssignAsync(Target, new(SeedData.RoleId(RoleNames.Manager), Start, null, null, null));

        Assert.Equal(ResultStatus.ValidationFailed, employee.Status);
        Assert.Equal(ResultStatus.ValidationFailed, manager.Status);

        using var context = harness.CreateContext();
        var systemAssignments = await context.UserRoles.Include(x => x.Role)
            .Where(x => x.TenantId == Tenant && (x.Role!.Name == RoleNames.Employee || x.Role.Name == RoleNames.Manager))
            .ToListAsync();
        foreach (var assignment in systemAssignments)
            Assert.Equal(ResultStatus.ValidationFailed, (await service.RevokeAsync(assignment.Id, new(null, "manual test"))).Status);
    }

    [Fact]
    public async Task Invalid_and_overlapping_periods_are_rejected_but_adjacent_periods_are_allowed()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        harness.TenantContext.UserId = Actor;
        var service = new RoleAssignmentService(harness.CreateContext(), harness.TenantContext, harness.Clock);
        var role = SeedData.RoleId(RoleNames.HRBP);

        var invalid = await service.AssignAsync(Target, new(role, new(2027, 2, 1), new(2027, 1, 31), null, null));
        var first = await service.AssignAsync(Target, new(role, new(2027, 1, 1), new(2027, 1, 31), null, null));
        var boundary = await service.AssignAsync(Target, new(role, new(2027, 1, 31), new(2027, 2, 28), null, null));
        var adjacent = await service.AssignAsync(Target, new(role, new(2027, 2, 1), new(2027, 2, 28), null, null));

        Assert.Equal(ResultStatus.ValidationFailed, invalid.Status);
        Assert.True(first.Succeeded, first.Message);
        Assert.Equal(ResultStatus.Conflict, boundary.Status);
        Assert.True(adjacent.Succeeded, adjacent.Message);
    }

    [Fact]
    public async Task Revocation_writes_an_immutable_audit_event_and_closes_the_period()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        harness.TenantContext.UserId = Actor;
        var service = new RoleAssignmentService(harness.CreateContext(), harness.TenantContext, harness.Clock);
        var assigned = await service.AssignAsync(Target, new(SeedData.RoleId(RoleNames.IT), Start, null, "temporary", null));
        Assert.True(assigned.Succeeded, assigned.Message);

        var revoked = await service.RevokeAsync(assigned.Value!.AssignmentId, new(new DateOnly(2027, 3, 1), "ended"));

        Assert.True(revoked.Succeeded, revoked.Message);
        using var context = harness.CreateUnscopedContext();
        var events = await context.UserRoleAssignmentEvents.IgnoreQueryFilters().Where(x => x.AssignmentId == assigned.Value.AssignmentId).ToListAsync();
        Assert.Equal(2, events.Count);
        Assert.Contains(events, x => x.EventType == UserRoleAssignmentEventType.Revoked && x.Reason == "ended");
    }

    [Fact]
    public async Task Invalid_scope_and_duplicate_scope_are_rejected()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        harness.TenantContext.UserId = Actor;
        var service = new RoleAssignmentService(harness.CreateContext(), harness.TenantContext, harness.Clock);
        var department = OrganizationTestHarness.DepartmentId(Tenant, "ENG");
        var duplicate = new RoleAssignmentScopeDto(RoleScopeType.Department, department);

        var result = await service.AssignAsync(Target, new(SeedData.RoleId(RoleNames.HRBP), Start, null, null, [duplicate, duplicate]));
        var missing = await service.AssignAsync(Target, new(SeedData.RoleId(RoleNames.HRBP), Start, null, null,
            [new(RoleScopeType.Department, Guid.NewGuid())]));

        Assert.Equal(ResultStatus.ValidationFailed, result.Status);
        Assert.Equal(ResultStatus.ValidationFailed, missing.Status);
    }

    [Fact]
    public async Task Super_hr_requires_existing_authority_and_cannot_be_self_assigned()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        harness.TenantContext.UserId = Actor;
        var service = new RoleAssignmentService(harness.CreateContext(), harness.TenantContext, harness.Clock);
        var request = new RoleAssignmentRequest(SeedData.RoleId(RoleNames.SuperHR), Start, null, "security", null);

        var unauthorized = await service.AssignAsync(Target, request);
        Assert.Equal(ResultStatus.Forbidden, unauthorized.Status);

        using (var context = harness.CreateContext())
        {
            context.UserRoles.Add(new UserRole { TenantId = Tenant, UserId = Actor, RoleId = SeedData.RoleId(RoleNames.SuperHR), EffectiveFrom = DateOnly.MinValue });
            await context.SaveChangesAsync();
        }

        var self = await service.AssignAsync(Actor, request);
        var allowed = await service.AssignAsync(Target, request);
        Assert.Equal(ResultStatus.Forbidden, self.Status);
        Assert.True(allowed.Succeeded, allowed.Message);
    }

    [Fact]
    public async Task Super_hr_revocation_preserves_the_last_active_assignment()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        harness.TenantContext.UserId = Actor;
        var roleId = SeedData.RoleId(RoleNames.SuperHR);
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        using (var context = harness.CreateContext())
        {
            context.UserRoles.AddRange(
                new UserRole { Id = firstId, TenantId = Tenant, UserId = Actor, RoleId = roleId, EffectiveFrom = DateOnly.MinValue },
                new UserRole { Id = secondId, TenantId = Tenant, UserId = Target, RoleId = roleId, EffectiveFrom = DateOnly.MinValue });
            await context.SaveChangesAsync();
        }
        var service = new RoleAssignmentService(harness.CreateContext(), harness.TenantContext, harness.Clock);

        Assert.True((await service.RevokeAsync(secondId, new(new DateOnly(2026, 3, 3), "transferred"))).Succeeded);
        using var verify = harness.CreateUnscopedContext();
        Assert.NotNull(await verify.UserRoles.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Id == firstId));
        var freshService = new RoleAssignmentService(harness.CreateContext(), harness.TenantContext, harness.Clock);
        var last = await freshService.RevokeAsync(firstId, new(null, "remove last"));
        Assert.Equal(ResultStatus.Conflict, last.Status);
    }

    [Fact]
    public async Task Role_assignment_history_is_tenant_isolated()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        harness.TenantContext.UserId = Actor;
        var service = new RoleAssignmentService(harness.CreateContext(), harness.TenantContext, harness.Clock);
        var assigned = await service.AssignAsync(Target, new(SeedData.RoleId(RoleNames.IT), Start, null, "tenant audit", null));
        Assert.True(assigned.Succeeded, assigned.Message);
        Assert.True((await service.GetAssignmentHistoryAsync(assigned.Value!.AssignmentId)).Succeeded);

        harness.ActAs(SeedData.TenantIds.Demo02);
        Assert.Equal(ResultStatus.NotFound, (await service.GetAssignmentHistoryAsync(assigned.Value.AssignmentId)).Status);
    }

    [Fact]
    public async Task Assignment_listing_returns_authoritative_statuses_and_server_side_filters()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        harness.TenantContext.UserId = Actor;
        var service = new RoleAssignmentService(harness.CreateContext(), harness.TenantContext, harness.Clock);
        var active = await service.AssignAsync(Target, new(SeedData.RoleId(RoleNames.HRBP), new(2026, 3, 4), new(2026, 3, 4), null, null));
        var scheduled = await service.AssignAsync(Target, new(SeedData.RoleId(RoleNames.IT), new(2027, 1, 1), null, null, null));
        var expired = await service.AssignAsync(Target, new(SeedData.RoleId(RoleNames.Accounts), new(2025, 1, 1), new(2025, 1, 31), null, null));
        var revoked = await service.AssignAsync(Target, new(SeedData.RoleId(RoleNames.EmployeeRelationshipOfficer), new(2025, 1, 1), null, null, null));
        Assert.True(active.Succeeded && scheduled.Succeeded && expired.Succeeded && revoked.Succeeded);
        Assert.True((await service.RevokeAsync(revoked.Value!.AssignmentId, new(new(2025, 6, 1), "ended"))).Succeeded);

        var page = await service.GetAssignmentsAsync(new RoleAssignmentQuery { Page = 1, PageSize = 2 });
        Assert.True(page.Succeeded, page.Message);
        Assert.True(page.Value!.TotalCount >= 4);
        Assert.Equal(2, page.Value.Items.Count);
        Assert.Equal(page.Value.Items.OrderByDescending(x => x.EffectiveFrom).ThenByDescending(x => x.AssignmentId).Select(x => x.AssignmentId), page.Value.Items.Select(x => x.AssignmentId));
        Assert.Contains((await service.GetAssignmentsAsync(new RoleAssignmentQuery { Status = "Active", RoleId = SeedData.RoleId(RoleNames.HRBP) })).Value!.Items, x => x.Status == "Active");
        Assert.Contains((await service.GetAssignmentsAsync(new RoleAssignmentQuery { Status = "Scheduled" })).Value!.Items, x => x.AssignmentId == scheduled.Value!.AssignmentId);
        Assert.Contains((await service.GetAssignmentsAsync(new RoleAssignmentQuery { Status = "Expired" })).Value!.Items, x => x.AssignmentId == expired.Value!.AssignmentId);
        var revokedPage = await service.GetAssignmentsAsync(new RoleAssignmentQuery { Status = "Revoked" });
        Assert.Contains(revokedPage.Value!.Items, x => x.AssignmentId == revoked.Value!.AssignmentId && x.IsRevoked && x.Status == "Revoked" && x.RevokedEffectiveDate == new DateOnly(2025, 6, 1));
    }

    [Fact]
    public async Task Role_management_candidates_are_tenant_scoped_and_do_not_require_link_permissions()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        harness.TenantContext.UserId = Actor;
        var service = new RoleAssignmentService(harness.CreateContext(), harness.TenantContext, harness.Clock);

        var candidates = await service.GetCandidatesAsync(new RoleAssignmentQuery { Page = 1, PageSize = 100 });
        Assert.True(candidates.Succeeded, candidates.Message);
        Assert.DoesNotContain(candidates.Value!.Items, x => x.UserId == Actor);
        Assert.All(candidates.Value.Items, x => Assert.NotEqual(Guid.Empty, x.UserId));

        harness.ActAs(SeedData.TenantIds.Demo02);
        var otherTenant = await service.GetCandidatesAsync(new RoleAssignmentQuery { Page = 1, PageSize = 100 });
        Assert.True(otherTenant.Succeeded, otherTenant.Message);
        Assert.DoesNotContain(otherTenant.Value!.Items, x => x.UserId == Target);
    }
}
