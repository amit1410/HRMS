using HRMS.Application.Abstractions;

namespace HRMS.Infrastructure.Security;

internal sealed class NullPlatformContext : IPlatformContext
{
    public Guid? UserId => null;
    public int? SecurityRevision => null;
    public bool HasSecurityRevision => false;
}
