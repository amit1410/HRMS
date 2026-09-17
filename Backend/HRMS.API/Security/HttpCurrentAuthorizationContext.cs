using HRMS.Application.Abstractions;
using HRMS.Application.Security;

namespace HRMS.API.Security;

public sealed class HttpCurrentAuthorizationContext(IHttpContextAccessor httpContextAccessor) : ICurrentAuthorizationContext
{
    public bool HasAnyPermission(params string[] permissions)
    {
        var user = httpContextAccessor.HttpContext?.User;
        return user?.Identity is { IsAuthenticated: true }
            && permissions.Any(permission => user.HasClaim(HrmsClaimTypes.Permission, permission));
    }
}
