using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Confirmation;
using HRMS.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

[ApiController, Route("api/confirmation-rating-scales"), Produces("application/json")]
public sealed class ConfirmationRatingScalesController : ControllerBase
{
    private readonly IConfirmationRatingScaleService _service;
    public ConfirmationRatingScalesController(IConfirmationRatingScaleService service) => _service = service;

    [HttpGet, HasPermission(Permissions.Confirmation.RatingScaleView)]
    public async Task<ActionResult<ApiResponse<PagedResult<ConfirmationRatingScaleDto>>>> GetAll([FromQuery] ConfirmationRatingScaleQuery query, CancellationToken ct) => (await _service.GetScalesAsync(query, ct)).ToActionResult();

    [HttpGet("{scaleId:guid}", Name = nameof(GetConfirmationRatingScale)), HasPermission(Permissions.Confirmation.RatingScaleView)]
    public async Task<ActionResult<ApiResponse<ConfirmationRatingScaleDetailDto>>> GetConfirmationRatingScale(Guid scaleId, CancellationToken ct) => (await _service.GetScaleAsync(scaleId, ct)).ToActionResult();

    [HttpPost, HasPermission(Permissions.Confirmation.RatingScaleManage)]
    public async Task<ActionResult<ApiResponse<ConfirmationRatingScaleDetailDto>>> Create([FromBody] ConfirmationRatingScaleRequest request, CancellationToken ct) => (await _service.CreateScaleAsync(request, ct)).ToCreatedResult(nameof(GetConfirmationRatingScale), x => new { scaleId = x.Scale.Id });

    [HttpPut("{scaleId:guid}"), HasPermission(Permissions.Confirmation.RatingScaleManage)]
    public async Task<ActionResult<ApiResponse<ConfirmationRatingScaleDetailDto>>> Update(Guid scaleId, [FromBody] ConfirmationRatingScaleRequest request, CancellationToken ct) => (await _service.UpdateScaleAsync(scaleId, request, ct)).ToActionResult();
}
