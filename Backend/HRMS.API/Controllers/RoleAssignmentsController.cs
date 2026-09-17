using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Roles;
using HRMS.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

[ApiController, Route("api/role-assignments")]
public sealed class RoleAssignmentsController(IRoleAssignmentService service) : ControllerBase
{
    [HttpGet][HasPermission(Permissions.RoleManagement.AssignmentView)]
    public async Task<ActionResult<ApiResponse<PagedResult<RoleAssignmentDto>>>> Assignments([FromQuery] RoleAssignmentQuery query, CancellationToken ct) => (await service.GetAssignmentsAsync(query, ct)).ToActionResult();
    [HttpGet("candidates")][HasPermission(Permissions.RoleManagement.AssignmentManage)]
    public async Task<ActionResult<ApiResponse<PagedResult<RoleManagementCandidateDto>>>> Candidates([FromQuery] PagedQueryModel query, CancellationToken ct) => (await service.GetCandidatesAsync(query, ct)).ToActionResult();
    [HttpGet("roles")][HasPermission(Permissions.RoleManagement.AssignmentView)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<RoleSummary>>>> Roles([FromQuery] bool assignableOnly, CancellationToken ct) => (await service.GetRolesAsync(assignableOnly, ct)).ToActionResult();
    [HttpGet("users/{userId:guid}")][HasPermission(Permissions.RoleManagement.AssignmentView)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<RoleAssignmentDto>>>> GetUserAssignments(Guid userId, CancellationToken ct) => (await service.GetUserAssignmentsAsync(userId, ct)).ToActionResult();
    [HttpPost("users/{userId:guid}")][HasPermission(Permissions.RoleManagement.AssignmentManage)]
    public async Task<ActionResult<ApiResponse<RoleAssignmentDto>>> Assign(Guid userId, RoleAssignmentRequest request, CancellationToken ct) => (await service.AssignAsync(userId, request, ct)).ToActionResult();
    [HttpPost("{assignmentId:guid}/revoke")][HasPermission(Permissions.RoleManagement.AssignmentManage)]
    public async Task<ActionResult<ApiResponse<RoleAssignmentDto>>> Revoke(Guid assignmentId, RoleAssignmentRevokeRequest request, CancellationToken ct) => (await service.RevokeAsync(assignmentId, request, ct)).ToActionResult();
    [HttpGet("{assignmentId:guid}/history")][HasPermission(Permissions.RoleManagement.AssignmentViewHistory)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<RoleAssignmentHistoryDto>>>> History(Guid assignmentId, CancellationToken ct) => (await service.GetAssignmentHistoryAsync(assignmentId, ct)).ToActionResult();
    [HttpGet("users/{userId:guid}/history")][HasPermission(Permissions.RoleManagement.AssignmentViewHistory)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<RoleAssignmentHistoryDto>>>> UserHistory(Guid userId, CancellationToken ct) => (await service.GetUserHistoryAsync(userId, ct)).ToActionResult();
}
