using System.Security.Claims;
using HRMS.Application.Abstractions;
using HRMS.Application.Security;

namespace HRMS.API.Security;

/// <summary>
/// Resolves the current tenant/user from the authenticated principal's JWT claims. This is the only
/// source of tenant identity at runtime — TenantId is never read from request bodies, query strings
/// or headers supplied by the client. Returns nulls when there is no authenticated user (e.g. the
/// login endpoint), which the DbContext treats as "no tenant resolved".
/// </summary>
public class HttpTenantContext : ITenantExecutionContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private Guid? _executionTenantId;

    public HttpTenantContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid? TenantId => _executionTenantId ?? ReadGuidClaim(HrmsClaimTypes.TenantId);

    public Guid? UserId => ReadGuidClaim(HrmsClaimTypes.UserId);

    public bool HasTenant => TenantId.HasValue;

    public void Use(Guid tenantId) => _executionTenantId = tenantId;

    private Guid? ReadGuidClaim(string claimType)
    {
        var user = _httpContextAccessor.HttpContext?.User;

        // Only an authenticated identity may contribute tenant identity. Claims present on an
        // unauthenticated principal (a rejected token, or anything set earlier in the pipeline) are
        // ignored, so an unverified request can never resolve a tenant.
        if (user?.Identity is not { IsAuthenticated: true })
        {
            return null;
        }

        var value = user.FindFirstValue(claimType);
        return Guid.TryParse(value, out var parsed) ? parsed : null;
    }
}
