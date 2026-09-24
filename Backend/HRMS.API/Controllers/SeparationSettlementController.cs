using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Separation;
using HRMS.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

[ApiController, Route("api/separation")]
public sealed class SeparationSettlementController(ISeparationSettlementOrchestrationService service) : ControllerBase
{
    [HttpGet("{id:guid}/settlement/readiness"), HasPermission(Permissions.Separation.ViewAll)]
    public async Task<ActionResult<ApiResponse<SeparationSettlementReadinessDto>>> Readiness(Guid id, CancellationToken ct) => (await service.GetReadinessAsync(id, ct)).ToActionResult();

    [HttpGet("{id:guid}/settlement"), HasPermission(Permissions.Separation.ViewAll)]
    public async Task<ActionResult<ApiResponse<SeparationSettlementStatusDto>>> Status(Guid id, CancellationToken ct) => (await service.GetStatusAsync(id, ct)).ToActionResult();

    [HttpPost("{id:guid}/settlement/initiate"), HasPermission(Permissions.Separation.Manage)]
    public async Task<ActionResult<ApiResponse<SeparationSettlementStatusDto>>> Initiate(Guid id, SettlementInitiationRequest request, CancellationToken ct) => (await service.InitiateAsync(id, request, ct)).ToActionResult();

    [HttpPost("{id:guid}/settlement/retry"), HasPermission(Permissions.Separation.Manage)]
    public async Task<ActionResult<ApiResponse<SeparationSettlementStatusDto>>> Retry(Guid id, SettlementRetryRequest request, CancellationToken ct) => (await service.RetryAsync(id, request, ct)).ToActionResult();

    [HttpGet("{id:guid}/settlement/history"), HasPermission(Permissions.Separation.ViewHistory)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<SeparationSettlementEventDto>>>> History(Guid id, CancellationToken ct) => (await service.GetHistoryAsync(id, ct)).ToActionResult();

    [HttpGet("settlement/dashboard"), HasPermission(Permissions.Separation.ViewAll)]
    public async Task<ActionResult<ApiResponse<PagedResult<SeparationSettlementDashboardItemDto>>>> Dashboard([FromQuery] SettlementDashboardQuery query, CancellationToken ct) => (await service.GetDashboardAsync(query, ct)).ToActionResult();
}
