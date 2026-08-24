using Microsoft.AspNetCore.Authorization;

namespace HRMS.API.Security;

/// <summary>
/// Requires the caller's token to carry a specific permission claim, e.g.
/// <c>[HasPermission(Permissions.Employee.Create)]</c>.
/// <para>
/// Authorizing on permissions rather than role names means an administrator can change what a role can
/// do without any code change, and endpoints never hard-code role checks. Policy names are the
/// permission names themselves, registered in <see cref="AuthenticationServiceCollectionExtensions"/>.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class HasPermissionAttribute : AuthorizeAttribute
{
    public HasPermissionAttribute(string permission) : base(permission)
    {
        Permission = permission;
    }

    public string Permission { get; }
}
