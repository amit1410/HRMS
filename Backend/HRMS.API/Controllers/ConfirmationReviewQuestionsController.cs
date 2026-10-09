using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Confirmation;
using HRMS.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

[ApiController, Route("api/confirmation-review-questions"), Produces("application/json")]
public sealed class ConfirmationReviewQuestionsController : ControllerBase
{
    private readonly IConfirmationReviewQuestionService _service;
    public ConfirmationReviewQuestionsController(IConfirmationReviewQuestionService service) => _service = service;

    [HttpGet, HasPermission(Permissions.Confirmation.ReviewQuestionView)]
    public async Task<ActionResult<ApiResponse<PagedResult<ConfirmationReviewQuestionDto>>>> GetAll([FromQuery] ConfirmationReviewQuestionQuery query, CancellationToken ct) => (await _service.GetQuestionsAsync(query, ct)).ToActionResult();

    [HttpGet("{questionId:guid}", Name = nameof(GetConfirmationReviewQuestion)), HasPermission(Permissions.Confirmation.ReviewQuestionView)]
    public async Task<ActionResult<ApiResponse<ConfirmationReviewQuestionDetailDto>>> GetConfirmationReviewQuestion(Guid questionId, CancellationToken ct) => (await _service.GetQuestionAsync(questionId, ct)).ToActionResult();

    [HttpPost, HasPermission(Permissions.Confirmation.ReviewQuestionManage)]
    public async Task<ActionResult<ApiResponse<ConfirmationReviewQuestionDetailDto>>> Create([FromBody] ConfirmationReviewQuestionRequest request, CancellationToken ct) => (await _service.CreateQuestionAsync(request, ct)).ToCreatedResult(nameof(GetConfirmationReviewQuestion), x => new { questionId = x.Question.Id });

    [HttpPut("{questionId:guid}"), HasPermission(Permissions.Confirmation.ReviewQuestionManage)]
    public async Task<ActionResult<ApiResponse<ConfirmationReviewQuestionDetailDto>>> Update(Guid questionId, [FromBody] ConfirmationReviewQuestionRequest request, CancellationToken ct) => (await _service.UpdateQuestionAsync(questionId, request, ct)).ToActionResult();
}
