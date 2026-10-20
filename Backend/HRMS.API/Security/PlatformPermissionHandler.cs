using System.Security.Claims;
using HRMS.Application.Abstractions;
using HRMS.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace HRMS.API.Security;

public sealed record PlatformPermissionRequirement(string Permission) : IAuthorizationRequirement;

public sealed class PlatformPermissionHandler : AuthorizationHandler<PlatformPermissionRequirement>
{
    private readonly IServiceProvider _services;

    public PlatformPermissionHandler(IServiceProvider services) => _services = services;

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PlatformPermissionRequirement requirement)
    {
        if (!Guid.TryParse(context.User.FindFirstValue("sub"), out var userId) ||
            context.User.FindFirstValue("auth_scope") != "platform") return;
        var catalog = _services.GetRequiredService<IHrmsCatalogDbContext>();
        var user = await catalog.PlatformUsers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId && x.IsActive);
        if (user is null) return;
        if (!int.TryParse(context.User.FindFirstValue("security_revision"), out var revision) || revision != user.SecurityRevision) return;
        var allowed = await (from ur in catalog.PlatformUserRoles
                             join rp in catalog.PlatformRolePermissions on ur.PlatformRoleId equals rp.PlatformRoleId
                             join p in catalog.PlatformPermissions on rp.PlatformPermissionId equals p.Id
                             where ur.PlatformUserId == userId && p.Name == requirement.Permission
                             select p.Id).AnyAsync();
        if (allowed) context.Succeed(requirement);
    }
}
