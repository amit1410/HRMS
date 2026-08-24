using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Tenants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace HRMS.API.Controllers;

/// <summary>
/// Organization-level endpoints that a client needs <em>before</em> it has a token.
/// <para>
/// There is exactly one, and it is read-only. Which organization it answers for is never in the URL: before
/// a token exists the host decides, and afterwards the validated token decides. Neither is something a
/// caller states, which is why no route here has an organization segment and no handler here takes one.
/// </para>
/// </summary>
[ApiController]
[Route("api/tenants")]
[Produces("application/json")]
public class TenantsController : ControllerBase
{
    private readonly ITenantBrandingService _brandingService;

    public TenantsController(ITenantBrandingService brandingService)
    {
        _brandingService = brandingService;
    }

    /// <summary>Returns the branding to show on the sign-in screen at this address.</summary>
    /// <remarks>
    /// Always <c>200</c>, never <c>404</c>. An address no organization uses, an organization that is not
    /// active and one that has not published its branding all return the same empty response. See
    /// <c>TenantBrandingService</c>.
    /// <para>
    /// <c>current</c> is a literal, not a placeholder: there is no variant of this route that names an
    /// organization. A caller can only be shown the branding of the address it is visiting, so the endpoint
    /// cannot be used to ask about anybody else.
    /// </para>
    /// <para>
    /// Anonymous by necessity — it is read to decide what the login form should look like — and therefore
    /// in the same rate-limit bucket as the credential endpoints, so a client that re-reads it in a loop is
    /// throttled by the limiter that already throttles guessing passwords.
    /// </para>
    /// </remarks>
    [HttpGet("current/branding")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingPolicies.Authentication)]
    [ProducesResponseType(typeof(ApiResponse<TenantBrandingDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ApiResponse<TenantBrandingDto>>> GetCurrentBranding(
        CancellationToken cancellationToken)
    {
        var result = await _brandingService.GetForCurrentOrganizationAsync(cancellationToken);
        return result.ToActionResult();
    }
}
