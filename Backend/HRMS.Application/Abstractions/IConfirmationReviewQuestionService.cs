using HRMS.Application.Common;
using HRMS.Application.DTOs.Confirmation;

namespace HRMS.Application.Abstractions;

/// <summary>
/// CRUD for tenant-configurable confirmation review questions, their options (Dropdown/MultiSelect) and
/// applicability rows. Mirrors the confirmation/leave configuration service conventions. Foundation only:
/// review answers, resolution into a review case, and decision logic are later Phase 9c sub-phases.
/// </summary>
public interface IConfirmationReviewQuestionService
{
    Task<Result<PagedResult<ConfirmationReviewQuestionDto>>> GetQuestionsAsync(ConfirmationReviewQuestionQuery query, CancellationToken ct = default);
    Task<Result<ConfirmationReviewQuestionDetailDto>> GetQuestionAsync(Guid questionId, CancellationToken ct = default);
    Task<Result<ConfirmationReviewQuestionDetailDto>> CreateQuestionAsync(ConfirmationReviewQuestionRequest request, CancellationToken ct = default);
    Task<Result<ConfirmationReviewQuestionDetailDto>> UpdateQuestionAsync(Guid questionId, ConfirmationReviewQuestionRequest request, CancellationToken ct = default);
}
