using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Separation;
using HRMS.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

[ApiController, Route("api/separation/exit")]
public sealed class SeparationExitController(ISeparationExitService service) : ControllerBase
{
    [HttpGet("{separationId:guid}/readiness"), HasPermission(Permissions.Separation.ViewAll)]
    public async Task<ActionResult<ApiResponse<SeparationExitReadinessDto>>> Readiness(Guid separationId, CancellationToken ct) => (await service.GetReadinessAsync(separationId, ct)).ToActionResult();

    [HttpGet("{separationId:guid}"), HasPermission(Permissions.Separation.ViewAll)]
    public async Task<ActionResult<ApiResponse<SeparationExitExecutionDto>>> Get(Guid separationId, CancellationToken ct) => (await service.GetAsync(separationId, ct)).ToActionResult();

    [HttpPost("{separationId:guid}/execute"), HasPermission(Permissions.Separation.Manage)]
    public async Task<ActionResult<ApiResponse<SeparationExitExecutionDto>>> Execute(Guid separationId, SeparationExitExecuteRequest request, CancellationToken ct) => (await service.ExecuteAsync(separationId, request, ct)).ToActionResult();

    [HttpPost("{separationId:guid}/retry"), HasPermission(Permissions.Separation.Manage)]
    public async Task<ActionResult<ApiResponse<SeparationExitExecutionDto>>> Retry(Guid separationId, SeparationExitRetryRequest request, CancellationToken ct) => (await service.RetryAsync(separationId, request, ct)).ToActionResult();

    [HttpGet("{separationId:guid}/history"), HasPermission(Permissions.Separation.ViewHistory)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<SeparationExitExecutionEventDto>>>> History(Guid separationId, CancellationToken ct) => (await service.HistoryAsync(separationId, ct)).ToActionResult();

    [HttpGet("dashboard"), HasPermission(Permissions.Separation.ViewAll)]
    public async Task<ActionResult<ApiResponse<PagedResult<SeparationExitDashboardItemDto>>>> Dashboard([FromQuery] SeparationExitDashboardQuery query, CancellationToken ct) => (await service.DashboardAsync(query, ct)).ToActionResult();
}
