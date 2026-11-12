using HRMS.Application.Abstractions;
using HRMS.Application.Security;

namespace HRMS.API.Security;

public sealed class HttpCurrentAuthorizationContext(
    IHttpContextAccessor httpContextAccessor,
    IEmployeeImportWorkerExecutionContext workerContext) : ICurrentAuthorizationContext
{
    public bool HasAnyPermission(params string[] permissions)
    {
        var user = httpContextAccessor.HttpContext?.User;
        if (user?.Identity is { IsAuthenticated: true })
            return permissions.Any(permission => user.HasClaim(HrmsClaimTypes.Permission, permission));
        return workerContext.HasAnyPermission(permissions);
    }
}
