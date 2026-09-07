using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.PlatformAuth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

[ApiController]
[Route("api/platform/auth")]
[Produces("application/json")]
public sealed class PlatformAuthController : ControllerBase
{
    private readonly IPlatformAuthService _service;
    public PlatformAuthController(IPlatformAuthService service) => _service = service;

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<PlatformLoginResponse>>> Login(PlatformLoginRequest request, CancellationToken ct) =>
        (await _service.LoginAsync(request, ct)).ToActionResult();

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<PlatformLoginResponse>>> Refresh(PlatformRefreshRequest request, CancellationToken ct) =>
        (await _service.RefreshAsync(request, ct)).ToActionResult();

    [HttpPost("logout")]
    [Authorize(AuthenticationSchemes = "PlatformBearer", Policy = "PlatformAuthenticated")]
    public async Task<ActionResult<ApiResponse<bool>>> Logout(PlatformLogoutRequest request, CancellationToken ct) =>
        (await _service.LogoutAsync(request, ct)).ToActionResult();

    [HttpGet("me")]
    [Authorize(AuthenticationSchemes = "PlatformBearer", Policy = "PlatformAuthenticated")]
    public async Task<ActionResult<ApiResponse<PlatformIdentityDto>>> Me(CancellationToken ct) =>
        (await _service.GetCurrentUserAsync(ct)).ToActionResult();
}
