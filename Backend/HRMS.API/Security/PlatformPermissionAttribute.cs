using Microsoft.AspNetCore.Authorization;

namespace HRMS.API.Security;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class PlatformPermissionAttribute : AuthorizeAttribute
{
    public PlatformPermissionAttribute(string permission)
    {
        Policy = permission;
        AuthenticationSchemes = "PlatformBearer";
    }
}
