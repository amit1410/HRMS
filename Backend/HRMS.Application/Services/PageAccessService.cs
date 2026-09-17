using HRMS.Application.Abstractions;
using HRMS.Application.Authorization;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Authorization;
using HRMS.Domain.Authorization;
using Microsoft.EntityFrameworkCore;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using System.Data;

namespace HRMS.Application.Services;

public sealed class PageAccessService(
    IHrmsDbContext db,
    ITenantContext tenantContext,
    IRoleResolutionService roleResolution,
    TimeProvider timeProvider) : IPageAccessService
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
        if (requested.Any(permission => !allowed.Contains(permission)))
            return Result<PageAccessMatrixDto>.Invalid("permissions", "The request contains an unknown permission.");

        var role = await db.Roles.SingleOrDefaultAsync(x => x.Id == roleId, cancellationToken);
        if (role is null) return Result<PageAccessMatrixDto>.NotFound("Role not found.");

        var permissionIds = await db.Permissions.Where(x => requested.Contains(x.Name)).ToDictionaryAsync(x => x.Name, x => x.Id, cancellationToken);
        var existing = await db.RolePermissions.Where(x => x.RoleId == roleId).ToListAsync(cancellationToken);
        var desiredIds = requested.Select(name => permissionIds[name]).ToHashSet();
        db.RolePermissions.RemoveRange(existing.Where(x => !desiredIds.Contains(x.PermissionId)));
        var existingIds = existing.Select(x => x.PermissionId).ToHashSet();
        var actorId = tenantContext.UserId;
        if (actorId is not Guid actor) return Result<PageAccessMatrixDto>.Unauthorized("No authenticated user.");
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
        var grantedSet = granted.ToHashSet(StringComparer.Ordinal);
        var pages = ApplicationPageCatalog.All.Select(page => new PageAccessPageDto(
            page.Code, page.Name, page.ModuleCode, page.Route, page.RequiredPermissions,
            page.RequiredPermissions.Any(grantedSet.Contains),
            page.Actions.Select(action => new PageAccessActionDto(action.Code, action.Name, action.Permission, grantedSet.Contains(action.Permission))).ToList())).ToList();
        return Result<PageAccessMatrixDto>.Success(new(new(role.Id, role.Name, role.Description), pages, granted));
    }
}
