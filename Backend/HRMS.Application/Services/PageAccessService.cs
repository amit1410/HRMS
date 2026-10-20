using HRMS.Application.Abstractions;
using HRMS.Application.Authorization;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Authorization;
using HRMS.Domain.Authorization;
using Microsoft.EntityFrameworkCore;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using System.Data;
using Microsoft.Extensions.Logging;

namespace HRMS.Application.Services;

public sealed class PageAccessService(
    IHrmsDbContext db,
    ITenantContext tenantContext,
    IRoleResolutionService roleResolution,
    TimeProvider timeProvider,
    ILogger<PageAccessService>? logger = null,
    IPermissionCatalogProvisioner? permissionCatalogProvisioner = null) : IPageAccessService
{
    public async Task<Result<IReadOnlyList<PageAccessRoleDto>>> GetRolesAsync(CancellationToken cancellationToken = default)
    {
        if (!tenantContext.HasTenant) return Result<IReadOnlyList<PageAccessRoleDto>>.Unauthorized("No authenticated tenant.");
        var roles = await db.Roles.AsNoTracking().OrderBy(x => x.Name)
            .Select(x => new PageAccessRoleDto(x.Id, x.Name, x.Description)).ToListAsync(cancellationToken);
        return Result<IReadOnlyList<PageAccessRoleDto>>.Success(roles);
    }

    public async Task<Result<PageAccessMatrixDto>> GetMatrixAsync(int roleId, CancellationToken cancellationToken = default)
    {
        if (!tenantContext.HasTenant) return Result<PageAccessMatrixDto>.Unauthorized("No authenticated tenant.");
        return await BuildMatrixAsync(roleId, cancellationToken);
    }

    public async Task<Result<PageAccessMatrixDto>> UpdateAsync(int roleId, PageAccessUpdateRequest request, CancellationToken cancellationToken = default)
    {
        if (!tenantContext.HasTenant) return Result<PageAccessMatrixDto>.Unauthorized("No authenticated tenant.");
        var allowed = Permissions.All.ToHashSet(StringComparer.Ordinal);
        var requested = request.Permissions.Distinct(StringComparer.Ordinal).ToArray();
        var unknown = requested.Where(permission => !allowed.Contains(permission)).ToArray();
        if (unknown.Length > 0)
        {
            logger?.LogWarning(
                "Page access update rejected unknown permission codes. Operation={Operation} TenantId={TenantId} RoleId={RoleId} RejectedPermissionCount={RejectedPermissionCount}",
                "UpdateRolePermissions", tenantContext.TenantId, roleId, unknown.Length);
            return Result<PageAccessMatrixDto>.Invalid("permissions", "The request contains an unknown permission.");
        }

        var role = await db.Roles.SingleOrDefaultAsync(x => x.Id == roleId, cancellationToken);
        if (role is null) return Result<PageAccessMatrixDto>.NotFound("Role not found.");
        var actorId = tenantContext.UserId;
        if (actorId is not Guid actor) return Result<PageAccessMatrixDto>.Unauthorized("No authenticated user.");

        // Catalog repair and role changes are separate actions. A first save that encounters
        // missing codes repairs reference rows only; the user must review and save again to
        // apply the selected grants after the refreshed matrix confirms provisioning succeeded.
        var provisionedBeforeRepair = await db.Permissions.AsNoTracking().Select(x => x.Name).ToListAsync(cancellationToken);
        var missingBeforeRepair = requested.Where(permission => !provisionedBeforeRepair.Contains(permission, StringComparer.Ordinal)).ToArray();
        if (missingBeforeRepair.Length > 0)
        {
            if (permissionCatalogProvisioner is null)
                return Result<PageAccessMatrixDto>.Invalid("permissions", "Some selected permissions are not provisioned. Contact an administrator to repair this tenant's permission catalog.");

            try
            {
                await permissionCatalogProvisioner.EnsureProvisionedAsync(cancellationToken);
            }
            catch (HRMS.Application.Common.PermissionCatalogCollisionException)
            {
                logger?.LogWarning("Page access permission catalog collision. Operation={Operation} TenantId={TenantId} RoleId={RoleId}",
                    "UpdateRolePermissions", tenantContext.TenantId, roleId);
                return Result<PageAccessMatrixDto>.Conflict("The permission catalog needs administrator repair before this change can be saved.");
            }

            var repaired = await db.Permissions.AsNoTracking().Select(x => x.Name).ToListAsync(cancellationToken);
            if (missingBeforeRepair.Any(permission => !repaired.Contains(permission, StringComparer.Ordinal)))
                return Result<PageAccessMatrixDto>.Invalid("permissions", "Permission catalog repair did not complete. No role permissions were changed; retry or contact an administrator.");

            logger?.LogInformation("Page access permission catalog repaired without changing role grants. Operation={Operation} TenantId={TenantId} RoleId={RoleId} ProvisionedPermissionCount={ProvisionedPermissionCount}",
                "UpdateRolePermissions", tenantContext.TenantId, roleId, missingBeforeRepair.Length);
            return await BuildMatrixAsync(roleId, cancellationToken);
        }

        var permissionIds = await db.Permissions.Where(x => requested.Contains(x.Name)).ToDictionaryAsync(x => x.Name, x => x.Id, cancellationToken);
        var missing = requested.Where(permission => !permissionIds.ContainsKey(permission)).ToArray();
        if (missing.Length > 0)
        {
            logger?.LogWarning(
                "Page access catalog validation failed after provisioning attempt. Operation={Operation} TenantId={TenantId} RoleId={RoleId} ProvisionerInvoked={ProvisionerInvoked} MissingPermissionCount={MissingPermissionCount} RejectedPermissionCodes={RejectedPermissionCodes}",
                "UpdateRolePermissions", tenantContext.TenantId, roleId, permissionCatalogProvisioner is not null, missing.Length, string.Join(", ", missing));
            return Result<PageAccessMatrixDto>.Invalid("permissions", "One or more permissions are not provisioned in this tenant database.");
        }
        var existing = await db.RolePermissions.Where(x => x.RoleId == roleId).ToListAsync(cancellationToken);
        var desiredIds = requested.Select(name => permissionIds[name]).ToHashSet();
        db.RolePermissions.RemoveRange(existing.Where(x => !desiredIds.Contains(x.PermissionId)));
        var existingIds = existing.Select(x => x.PermissionId).ToHashSet();
        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim()[..Math.Min(request.Reason.Trim().Length, 500)];
        foreach (var id in desiredIds.Where(id => !existingIds.Contains(id)))
        {
            db.RolePermissions.Add(new() { RoleId = roleId, PermissionId = id });
            db.AuthorizationConfigurationEvents.Add(new AuthorizationConfigurationEvent
            {
                Id = Guid.NewGuid(), TenantId = tenantContext.TenantId!.Value, EventType = AuthorizationConfigurationEventType.RolePermissionGranted,
                EntityType = nameof(RolePermission), RoleId = roleId, PermissionId = id, PermissionCode = permissionIds.Single(x => x.Value == id).Key,
                Action = "Granted", NewValue = "Granted", Reason = reason, ActorUserId = actor, OccurredAtUtc = timeProvider.GetUtcNow().UtcDateTime
            });
        }
        foreach (var row in existing.Where(x => !desiredIds.Contains(x.PermissionId)))
        {
            var code = await db.Permissions.Where(x => x.Id == row.PermissionId).Select(x => x.Name).SingleAsync(cancellationToken);
            db.AuthorizationConfigurationEvents.Add(new AuthorizationConfigurationEvent
            {
                Id = Guid.NewGuid(), TenantId = tenantContext.TenantId!.Value, EventType = AuthorizationConfigurationEventType.RolePermissionRevoked,
                EntityType = nameof(RolePermission), RoleId = roleId, PermissionId = row.PermissionId, PermissionCode = code,
                Action = "Revoked", OldValue = "Granted", Reason = reason, ActorUserId = actor, OccurredAtUtc = timeProvider.GetUtcNow().UtcDateTime
            });
        }
        await using (var tx = await db.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken))
        {
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        return await BuildMatrixAsync(roleId, cancellationToken);
    }

    public async Task<Result<PageAccessMatrixDto>> ReconcileApprovedDefaultsAsync(
        int roleId,
        PageAccessReconciliationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!tenantContext.HasTenant) return Result<PageAccessMatrixDto>.Unauthorized("No authenticated tenant.");
        if (request.Permissions is null || request.Permissions.Count == 0)
            return Result<PageAccessMatrixDto>.Invalid("permissions", "At least one permission is required.");

        // The eligibility and permission-shape checks above are read-only validation. Start the
        // serializable unit before reading the current grants and revocation history so concurrent
        // reconciliation cannot both observe the same missing row outside the protected interval.
        await using var tx = await db.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var role = await db.Roles.SingleOrDefaultAsync(x => x.Id == roleId, cancellationToken);
        if (role is null) return Result<PageAccessMatrixDto>.NotFound("Role not found.");

        // Only the two platform-defined full tenant roles are eligible. Custom roles and narrower
        // built-in roles must continue to be configured explicitly in Page Access Management.
        if (role.Name is not (RoleNames.SuperAdmin or RoleNames.TenantAdmin))
            return Result<PageAccessMatrixDto>.Invalid("roleId", "This role is not eligible for default-permission reconciliation.");

        var requested = request.Permissions.Distinct(StringComparer.Ordinal).ToArray();
        var allowed = Permissions.All
            .Where(permission => !permission.StartsWith("AccountEmployeeLink.", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
        if (requested.Any(permission => !allowed.Contains(permission)))
            return Result<PageAccessMatrixDto>.Invalid("permissions", "The request contains a permission outside the approved role policy.");

        if (tenantContext.UserId is not Guid actor)
            return Result<PageAccessMatrixDto>.Unauthorized("No authenticated user.");

        var permissionIds = await db.Permissions
            .Where(x => requested.Contains(x.Name))
            .ToDictionaryAsync(x => x.Name, x => x.Id, cancellationToken);
        if (permissionIds.Count != requested.Length)
            return Result<PageAccessMatrixDto>.Invalid("permissions", "One or more permissions are not provisioned in this tenant database.");

        var existingIds = await db.RolePermissions
            .Where(x => x.RoleId == roleId)
            .Select(x => x.PermissionId)
            .ToHashSetAsync(cancellationToken);
        var explicitlyRevoked = await db.AuthorizationConfigurationEvents
            .Where(x => x.RoleId == roleId && x.EventType == AuthorizationConfigurationEventType.RolePermissionRevoked
                && x.PermissionId != null && permissionIds.Values.Contains(x.PermissionId.Value))
            .Select(x => x.PermissionId!.Value)
            .ToHashSetAsync(cancellationToken);
        var reason = string.IsNullOrWhiteSpace(request.Reason) ? "Approved default-role reconciliation" : request.Reason.Trim()[..Math.Min(request.Reason.Trim().Length, 500)];
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var additions = requested
            .Select(permission => (permission, id: permissionIds[permission]))
            .Where(x => !existingIds.Contains(x.id) && !explicitlyRevoked.Contains(x.id))
            .ToArray();

        foreach (var addition in additions)
        {
            db.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionId = addition.id });
            db.AuthorizationConfigurationEvents.Add(new AuthorizationConfigurationEvent
            {
                Id = Guid.NewGuid(), TenantId = tenantContext.TenantId!.Value,
                EventType = AuthorizationConfigurationEventType.RolePermissionGranted,
                EntityType = nameof(RolePermission), RoleId = roleId, PermissionId = addition.id,
                PermissionCode = addition.permission, Action = "Reconciled", NewValue = "Granted",
                Reason = reason, ActorUserId = actor, OccurredAtUtc = now
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        return await BuildMatrixAsync(roleId, cancellationToken);
    }

    public async Task<Result<IReadOnlyList<NavigationItemDto>>> GetNavigationAsync(CancellationToken cancellationToken = default)
    {
        if (tenantContext.TenantId is not Guid tenantId || tenantContext.UserId is not Guid userId)
            return Result<IReadOnlyList<NavigationItemDto>>.Unauthorized("No authenticated tenant or user.");
        var date = DateOnly.FromDateTime(timeProvider.GetUtcNow().DateTime);
        var roleIds = await roleResolution.GetEffectiveRoleIdsAsync(tenantId, userId, date, cancellationToken);
        var permissions = await db.RolePermissions.Where(x => roleIds.Contains(x.RoleId)).Select(x => x.Permission!.Name).Distinct().ToListAsync(cancellationToken);
        var visiblePages = ApplicationPageCatalog.All.Where(page => page.RequiredPermissions.Any(permissions.Contains)).ToList();
        var visible = visiblePages.GroupBy(page => page.ModuleCode).Select(group => new NavigationItemDto(
            group.Key, group.Key, string.Empty, group.Key,
            group.Select(page => new NavigationItemDto(page.Code, page.Name, page.Route, page.ModuleCode, Array.Empty<NavigationItemDto>())).ToList())).ToList();
        return Result<IReadOnlyList<NavigationItemDto>>.Success(visible);
    }

    public async Task<Result<UserAccessPreviewDto>> GetUserPreviewAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (tenantContext.TenantId is not Guid tenantId) return Result<UserAccessPreviewDto>.Unauthorized("No authenticated tenant.");
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().DateTime);
        var assignments = await db.UserRoles.AsNoTracking().Include(x => x.Role).Include(x => x.Scopes)
            .Where(x => x.TenantId == tenantId && x.UserId == userId && x.EffectiveFrom <= today && (x.EffectiveTo == null || x.EffectiveTo >= today))
            .OrderBy(x => x.Role!.Name).ThenBy(x => x.EffectiveFrom).ToListAsync(cancellationToken);
        var roleIds = assignments.Select(x => x.RoleId).Distinct().ToArray();
        var permissions = await db.RolePermissions.AsNoTracking().Where(x => roleIds.Contains(x.RoleId)).Select(x => x.Permission!.Name).Distinct().OrderBy(x => x).ToListAsync(cancellationToken);
        var employeeId = await db.AccountEmployeeCurrentLinks.AsNoTracking().Where(x => x.TenantId == tenantId && x.UserId == userId).Select(x => (Guid?)x.EmployeeId).SingleOrDefaultAsync(cancellationToken);
        var roles = assignments.Select(x => new EffectiveRoleAccessDto(x.Id, x.RoleId, x.Role!.Name, x.AssignmentSource, x.EffectiveFrom, x.EffectiveTo, x.Scopes.Count == 0, x.Scopes.Select(scope => new RoleAssignmentScopePreviewDto(scope.ScopeType, scope.ScopeEntityId)).ToList())).ToList();
        var pages = ApplicationPageCatalog.All.Where(page => page.RequiredPermissions.Any(permissions.Contains)).Select(page => new NavigationItemDto(page.Code, page.Name, page.Route, page.ModuleCode, Array.Empty<NavigationItemDto>())).ToList();
        var hasManagerAccess = permissions.Contains(Permissions.Attendance.MonthlyViewTeam, StringComparer.Ordinal) && employeeId is not null;
        return Result<UserAccessPreviewDto>.Success(new(userId, employeeId, roles, permissions, pages, hasManagerAccess, roles.Count(x => x.TenantWide)));
    }

    public async Task<Result<IReadOnlyList<AccessPreviewUserDto>>> SearchUsersAsync(string? search, CancellationToken cancellationToken = default)
    {
        if (!tenantContext.HasTenant) return Result<IReadOnlyList<AccessPreviewUserDto>>.Unauthorized("No authenticated tenant.");
        var users = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var value = search.Trim();
            users = users.Where(x => x.Email.Contains(value) || x.FirstName.Contains(value) || x.LastName.Contains(value));
        }
        var result = await users.OrderBy(x => x.FirstName).ThenBy(x => x.LastName).Take(50)
            .Select(x => new AccessPreviewUserDto(x.Id, (x.FirstName + " " + x.LastName).Trim(), x.Email)).ToListAsync(cancellationToken);
        return Result<IReadOnlyList<AccessPreviewUserDto>>.Success(result);
    }

    public async Task<Result<PagedResult<PageAccessHistoryItemDto>>> GetHistoryAsync(PageAccessHistoryQuery query, CancellationToken cancellationToken = default)
    {
        if (!tenantContext.HasTenant) return Result<PagedResult<PageAccessHistoryItemDto>>.Unauthorized("No authenticated tenant.");
        if (query.Page < 1 || query.PageSize is < 1 or > PagedQuery.MaxPageSize)
            return Result<PagedResult<PageAccessHistoryItemDto>>.Invalid("page", "Page values are out of range.");
        var source = db.AuthorizationConfigurationEvents.AsNoTracking();
        if (query.RoleId is int roleId) source = source.Where(x => x.RoleId == roleId);
        if (query.UserId is Guid userId) source = source.Where(x => x.UserId == userId);
        if (query.UserRoleAssignmentId is Guid assignmentId) source = source.Where(x => x.UserRoleAssignmentId == assignmentId);
        if (!string.IsNullOrWhiteSpace(query.EventType) && Enum.TryParse<AuthorizationConfigurationEventType>(query.EventType, true, out var eventType)) source = source.Where(x => x.EventType == eventType);
        if (query.FromDate is DateTime from) source = source.Where(x => x.OccurredAtUtc >= from);
        if (query.ToDate is DateTime to) source = source.Where(x => x.OccurredAtUtc < to.Date.AddDays(1));
        var total = await source.CountAsync(cancellationToken);
        var rows = await source.OrderByDescending(x => x.OccurredAtUtc).ThenByDescending(x => x.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Select(x => new PageAccessHistoryItemDto(x.Id, x.OccurredAtUtc, x.EventType.ToString(), x.Action, x.RoleId,
                db.Roles.Where(role => role.Id == x.RoleId).Select(role => role.Name).FirstOrDefault(), x.UserId, x.UserRoleAssignmentId,
                x.PermissionCode, x.ScopeDimension.HasValue ? x.ScopeDimension.Value.ToString() : null, x.ScopeValueId, x.ScopeValueDisplay,
                x.OldValue, x.NewValue, x.ActorUserId,
                db.Users.Where(user => user.Id == x.ActorUserId).Select(user => (user.FirstName + " " + user.LastName).Trim()).FirstOrDefault(), x.Reason))
            .ToListAsync(cancellationToken);
        return Result<PagedResult<PageAccessHistoryItemDto>>.Success(new(rows, query.Page, query.PageSize, total));
    }

    private async Task<Result<PageAccessMatrixDto>> BuildMatrixAsync(int roleId, CancellationToken cancellationToken)
    {
        var role = await db.Roles.AsNoTracking().SingleOrDefaultAsync(x => x.Id == roleId, cancellationToken);
        if (role is null) return Result<PageAccessMatrixDto>.NotFound("Role not found.");
        var granted = await db.RolePermissions.Where(x => x.RoleId == roleId).Select(x => x.Permission!.Name).ToListAsync(cancellationToken);
        var provisioned = await db.Permissions.AsNoTracking().Select(x => x.Name).OrderBy(x => x).ToListAsync(cancellationToken);
        var grantedSet = granted.ToHashSet(StringComparer.Ordinal);
        var pages = ApplicationPageCatalog.All.Select(page => new PageAccessPageDto(
            page.Code, page.Name, page.ModuleCode, page.Route, page.RequiredPermissions,
            page.RequiredPermissions.Any(grantedSet.Contains),
            page.Actions.Select(action => new PageAccessActionDto(action.Code, action.Name, action.Permission, grantedSet.Contains(action.Permission))).ToList())).ToList();
        return Result<PageAccessMatrixDto>.Success(new(new(role.Id, role.Name, role.Description), pages, granted, provisioned));
    }
}
