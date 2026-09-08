using HRMS.API.Extensions;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Employees;
using HRMS.Application.DTOs.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

[ApiController]
[Route("api/me")]
[Produces("application/json")]
public sealed class MeController : ControllerBase
{
    private readonly IMyEmployeeProfileService _profiles;
    private readonly IPasswordRecoveryService _recovery;

    public MeController(IMyEmployeeProfileService profiles, IPasswordRecoveryService recovery) { _profiles = profiles; _recovery = recovery; }

    [HttpGet("profile")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<MyEmployeeProfileDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<MyEmployeeProfileDto>>> GetProfile(CancellationToken cancellationToken) =>
        (await _profiles.GetAsync(cancellationToken)).ToActionResult();

    [HttpPost("change-password")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<bool>>> ChangePassword(ChangePasswordRequest request, CancellationToken cancellationToken)
    { Response.Headers.CacheControl = "no-store"; return (await _recovery.ChangePasswordAsync(request, cancellationToken)).ToActionResult(); }
}
