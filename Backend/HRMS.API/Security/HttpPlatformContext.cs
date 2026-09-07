using System.Security.Claims;
using HRMS.Application.Abstractions;

namespace HRMS.API.Security;

public sealed class HttpPlatformContext(IHttpContextAccessor accessor) : IPlatformContext
{
    public Guid? UserId => Guid.TryParse(accessor.HttpContext?.User.FindFirstValue("sub"), out var id) ? id : null;
    public int? SecurityRevision => int.TryParse(accessor.HttpContext?.User.FindFirstValue("security_revision"), out var revision) ? revision : null;
    public bool HasSecurityRevision => accessor.HttpContext?.User.FindFirst("security_revision") is not null;
}
