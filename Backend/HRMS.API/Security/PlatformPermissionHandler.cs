using System.Security.Claims;
using HRMS.Application.Abstractions;
using HRMS.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace HRMS.API.Security;

public sealed record PlatformPermissionRequirement(string Permission) : IAuthorizationRequirement;

public sealed class PlatformPermissionHandler : AuthorizationHandler<PlatformPermissionRequirement>
{
    private readonly IHrmsCatalogDbContext _catalog;

    public PlatformPermissionHandler(IHrmsCatalogDbContext catalog) => _catalog = catalog;

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PlatformPermissionRequirement requirement)
    {
        if (!Guid.TryParse(context.User.FindFirstValue("sub"), out var userId) ||
            context.User.FindFirstValue("auth_scope") != "platform") return;
        var user = await _catalog.PlatformUsers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId && x.IsActive);
        if (user is null) return;
        if (!int.TryParse(context.User.FindFirstValue("security_revision"), out var revision) || revision != user.SecurityRevision) return;
        var allowed = await (from ur in _catalog.PlatformUserRoles
                             join rp in _catalog.PlatformRolePermissions on ur.PlatformRoleId equals rp.PlatformRoleId
                             join p in _catalog.PlatformPermissions on rp.PlatformPermissionId equals p.Id
                             where ur.PlatformUserId == userId && p.Name == requirement.Permission
                             select p.Id).AnyAsync();
        if (allowed) context.Succeed(requirement);
    }
}
