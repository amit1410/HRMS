using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Authorization;
using HRMS.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

[ApiController, Route("api/page-access")]
public sealed class PageAccessController(IPageAccessService service) : ControllerBase
{
    [HttpGet("roles"), HasPermission(Permissions.PageAccess.View)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<PageAccessRoleDto>>>> Roles(CancellationToken ct) => (await service.GetRolesAsync(ct)).ToActionResult();

    [HttpGet("roles/{roleId:int}"), HasPermission(Permissions.PageAccess.View)]
    public async Task<ActionResult<ApiResponse<PageAccessMatrixDto>>> Matrix(int roleId, CancellationToken ct) => (await service.GetMatrixAsync(roleId, ct)).ToActionResult();

    [HttpPut("roles/{roleId:int}"), HasPermission(Permissions.PageAccess.Manage)]
    public async Task<ActionResult<ApiResponse<PageAccessMatrixDto>>> Update(int roleId, PageAccessUpdateRequest request, CancellationToken ct) => (await service.UpdateAsync(roleId, request, ct)).ToActionResult();

    [HttpGet("users/{userId:guid}/preview"), HasPermission(Permissions.PageAccess.View)]
    public async Task<ActionResult<ApiResponse<UserAccessPreviewDto>>> UserPreview(Guid userId, CancellationToken ct) => (await service.GetUserPreviewAsync(userId, ct)).ToActionResult();

    [HttpGet("users"), HasPermission(Permissions.PageAccess.View)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<AccessPreviewUserDto>>>> Users([FromQuery] string? search, CancellationToken ct) => (await service.SearchUsersAsync(search, ct)).ToActionResult();

    [HttpGet("history"), HasPermission(Permissions.PageAccess.View)]
    public async Task<ActionResult<ApiResponse<PagedResult<PageAccessHistoryItemDto>>>> History([FromQuery] PageAccessHistoryQuery query, CancellationToken ct) => (await service.GetHistoryAsync(query, ct)).ToActionResult();
}

[ApiController, Route("api/auth/navigation")]
public sealed class NavigationController(IPageAccessService service) : ControllerBase
{
    [HttpGet, Authorize]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<NavigationItemDto>>>> Get(CancellationToken ct) => (await service.GetNavigationAsync(ct)).ToActionResult();
}
