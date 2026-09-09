using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.PlatformTenants;
using HRMS.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

/// <summary>Platform-only tenant lifecycle operations. Public tenant branding remains in TenantsController.</summary>
[ApiController]
[Route("api/platform/tenants")]
[Produces("application/json")]
public sealed class PlatformTenantsController : ControllerBase
{
    private readonly IPlatformTenantService _service;

    public PlatformTenantsController(IPlatformTenantService service) => _service = service;

    [HttpGet]
    [PlatformPermission(PlatformPermissions.TenantView)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<PlatformTenantListItemDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<PlatformTenantListItemDto>>>> List(CancellationToken cancellationToken) =>
        (await _service.ListAsync(cancellationToken)).ToActionResult();

    [HttpGet("{id:guid}")]
    [PlatformPermission(PlatformPermissions.TenantView)]
    [ProducesResponseType(typeof(ApiResponse<PlatformTenantDetailDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PlatformTenantDetailDto>>> Get(Guid id, CancellationToken cancellationToken) =>
        (await _service.GetAsync(id, cancellationToken)).ToActionResult();

    [HttpPost]
    [PlatformPermission(PlatformPermissions.TenantCreate)]
    [ProducesResponseType(typeof(ApiResponse<PlatformTenantDetailDto>), StatusCodes.Status201Created)]
    public async Task<ActionResult<ApiResponse<PlatformTenantDetailDto>>> Create(
        CreatePlatformTenantRequest request,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return (await _service.CreateAsync(request, cancellationToken))
            .ToCreatedResult(nameof(Get), result => new { id = result.Id });
    }

    [HttpPut("{id:guid}")]
    [PlatformPermission(PlatformPermissions.TenantCreate)]
    public async Task<ActionResult<ApiResponse<PlatformTenantDetailDto>>> UpdateInactive(
        Guid id,
        UpdateInactivePlatformTenantRequest request,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return (await _service.UpdateInactiveAsync(id, request, cancellationToken)).ToActionResult();
    }

    [HttpPost("{id:guid}/activate")]
    [PlatformPermission(PlatformPermissions.TenantUpdateStatus)]
    public async Task<ActionResult<ApiResponse<PlatformTenantDetailDto>>> Activate(Guid id, CancellationToken cancellationToken) =>
        (await _service.ActivateAsync(id, cancellationToken)).ToActionResult();

    [HttpPost("{id:guid}/deactivate")]
    [PlatformPermission(PlatformPermissions.TenantUpdateStatus)]
    public async Task<ActionResult<ApiResponse<PlatformTenantDetailDto>>> Deactivate(Guid id, CancellationToken cancellationToken) =>
        (await _service.DeactivateAsync(id, cancellationToken)).ToActionResult();

    [HttpPost("{id:guid}/retry-provisioning")]
    [PlatformPermission(PlatformPermissions.TenantCreate)]
    public async Task<ActionResult<ApiResponse<PlatformTenantDetailDto>>> RetryProvisioning(
        Guid id,
        RetryPlatformTenantRequest request,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return (await _service.RetryProvisioningAsync(id, request, cancellationToken)).ToActionResult();
    }

    [HttpPost("{id:guid}/reset-admin-password")]
    [PlatformPermission(PlatformPermissions.TenantCreate)]
    [ProducesResponseType(typeof(ApiResponse<ResetTenantAdminPasswordResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ResetTenantAdminPasswordResponse>>> ResetAdminPassword(
        Guid id,
        ResetTenantAdminPasswordRequest request,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return (await _service.ResetTenantAdminPasswordAsync(id, request, cancellationToken)).ToActionResult();
    }
}
