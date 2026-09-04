using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.AccountEmployeeLinks;
using HRMS.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

[ApiController]
[Route("api/account-employee-links")]
[Produces("application/json")]
public sealed class AccountEmployeeLinksController : ControllerBase
{
    private readonly IAccountEmployeeLinkService _service;
    public AccountEmployeeLinksController(IAccountEmployeeLinkService service) => _service = service;

    [HttpGet("users/{userId:guid}")]
    [HasPermission(Permissions.AccountEmployeeLink.View)]
    public async Task<ActionResult<ApiResponse<AccountEmployeeCurrentStateDto>>> GetUser(Guid userId, CancellationToken ct) => (await _service.GetUserAsync(userId, ct)).ToActionResult();

    [HttpGet("employees/{employeeId:guid}")]
    [HasPermission(Permissions.AccountEmployeeLink.View)]
    public async Task<ActionResult<ApiResponse<AccountEmployeeCurrentStateDto>>> GetEmployee(Guid employeeId, CancellationToken ct) => (await _service.GetEmployeeAsync(employeeId, ct)).ToActionResult();

    [HttpGet("candidates/users")]
    [HasPermission(Permissions.AccountEmployeeLink.View)]
    [HasPermission(Permissions.AccountEmployeeLink.Manage)]
    public async Task<ActionResult<ApiResponse<PagedResult<AccountEmployeeCandidateDto>>>> UserCandidates([FromQuery] AccountEmployeeQuery query, CancellationToken ct) => (await _service.GetUserCandidatesAsync(query, ct)).ToActionResult();

    [HttpGet("candidates/employees")]
    [HasPermission(Permissions.AccountEmployeeLink.View)]
    [HasPermission(Permissions.AccountEmployeeLink.Manage)]
    public async Task<ActionResult<ApiResponse<PagedResult<AccountEmployeeCandidateDto>>>> EmployeeCandidates([FromQuery] AccountEmployeeQuery query, CancellationToken ct) => (await _service.GetEmployeeCandidatesAsync(query, ct)).ToActionResult();

    [HttpGet("users/{userId:guid}/history")]
    [HasPermission(Permissions.AccountEmployeeLink.ViewHistory)]
    public async Task<ActionResult<ApiResponse<PagedResult<AccountEmployeeLinkEventDto>>>> History(Guid userId, [FromQuery] AccountEmployeeHistoryQuery query, CancellationToken ct) => (await _service.GetHistoryAsync(userId, query, ct)).ToActionResult();

    [HttpPost("users/{userId:guid}/link")]
    [HasPermission(Permissions.AccountEmployeeLink.View)]
    [HasPermission(Permissions.AccountEmployeeLink.Manage)]
    public async Task<ActionResult<ApiResponse<AccountEmployeeCurrentStateDto>>> Link(Guid userId, AccountEmployeeLinkRequest request, CancellationToken ct) => (await _service.LinkAsync(userId, request, ct)).ToActionResult();

    [HttpPost("users/{userId:guid}/unlink")]
    [HasPermission(Permissions.AccountEmployeeLink.View)]
    [HasPermission(Permissions.AccountEmployeeLink.Manage)]
    public async Task<ActionResult<ApiResponse<AccountEmployeeCurrentStateDto>>> Unlink(Guid userId, AccountEmployeeUnlinkRequest request, CancellationToken ct) => (await _service.UnlinkAsync(userId, request, ct)).ToActionResult();

    [HttpPost("users/{userId:guid}/replace")]
    [HasPermission(Permissions.AccountEmployeeLink.View)]
    [HasPermission(Permissions.AccountEmployeeLink.Manage)]
    public async Task<ActionResult<ApiResponse<AccountEmployeeCurrentStateDto>>> Replace(Guid userId, AccountEmployeeReplaceRequest request, CancellationToken ct) => (await _service.ReplaceAsync(userId, request, ct)).ToActionResult();
}
