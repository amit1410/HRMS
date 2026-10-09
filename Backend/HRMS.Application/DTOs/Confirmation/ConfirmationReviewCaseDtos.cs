using HRMS.Application.Common;
using HRMS.Domain.Enums;

namespace HRMS.Application.DTOs.Confirmation;

// --- Case (list + detail) ---

public sealed record ConfirmationReviewCaseDto(
    Guid Id,
    Guid EmployeeId,
    string? EmployeeCode,
    string? EmployeeName,
    Guid ConfirmationPolicyId,
    Guid ConfirmationPolicyVersionId,
    string ProbationCycleKey,
    int ReviewRound,
    DateOnly ProbationStartDate,
    DateOnly ProbationEndDate,
    DateOnly DueDate,
    ConfirmationReviewCaseStatus Status,
    Guid? ManagerReviewerEmployeeId,
    Guid? HrbpReviewerEmployeeId,
    int QuestionCount,
    DateTime CreatedDate,
    DateTime? ModifiedDate);

public sealed record ConfirmationReviewCaseQuestionDto(
    Guid Id,
    Guid SourceQuestionId,
    string Code,
    string QuestionText,
    string? HelpText,
    ReviewQuestionType QuestionType,
    bool IsMandatory,
    int DisplayOrder,
    Guid? SourceRatingScaleId,
    string? RatingScaleCode,
    string? RatingScaleName,
    int? RatingMin,
    int? RatingMax,
    IReadOnlyList<ConfirmationReviewCaseOptionDto> Options,
    IReadOnlyList<ConfirmationReviewCaseRatingValueDto> RatingValues);

public sealed record ConfirmationReviewCaseOptionDto(string Code, string Label, int DisplayOrder);

public sealed record ConfirmationReviewCaseRatingValueDto(int RatingValue, string Label, int DisplayOrder);

public sealed record ConfirmationReviewerReviewDto(
    Guid Id,
    ConfirmationReviewerType ReviewerType,
    Guid? ReviewerEmployeeId,
    int ReviewRound,
    int VersionNumber,
    ConfirmationReviewStatus Status,
    string? OverallComments,
    DateTime? SubmittedAtUtc,
    IReadOnlyList<ConfirmationReviewResponseDto> Responses);

public sealed record ConfirmationReviewResponseDto(
    Guid ConfirmationReviewCaseQuestionId,
    int? RatingValue,
    bool? BoolValue,
    string? TextValue,
    IReadOnlyList<string> SelectedOptionCodes);

public sealed record ConfirmationReviewCaseDetailDto(
    ConfirmationReviewCaseDto Case,
    IReadOnlyList<ConfirmationReviewCaseQuestionDto> Questions,
    IReadOnlyList<ConfirmationReviewerReviewDto> Reviews);

public sealed record ConfirmationReviewCaseEventDto(
    Guid Id,
    ConfirmationReviewCaseEventType EventType,
    Guid? ConfirmationReviewerReviewId,
    Guid? ActorUserId,
    string? Details,
    DateTime OccurredAtUtc);

public sealed class ConfirmationReviewCaseQuery : PagedQuery
{
    public Guid? EmployeeId { get; set; }
    public ConfirmationReviewCaseStatus? Status { get; set; }

    public static readonly IReadOnlyList<string> SortFields = new[] { "createddate", "duedate", "probationenddate", "status" };
}

// --- Requests ---

public sealed record CreateConfirmationReviewCaseRequest(
    Guid EmployeeId,
    DateOnly? EffectiveDate = null);

/// <summary>The reviewer form: the resolved snapshot questions plus the current draft/submitted review (if any).</summary>
public sealed record ConfirmationReviewerFormDto(
    ConfirmationReviewCaseDto Case,
    ConfirmationReviewerType ReviewerType,
    IReadOnlyList<ConfirmationReviewCaseQuestionDto> Questions,
    ConfirmationReviewerReviewDto? CurrentReview);

public sealed record ConfirmationReviewResponseInput(
    Guid ConfirmationReviewCaseQuestionId,
    int? RatingValue = null,
    bool? BoolValue = null,
    string? TextValue = null,
    IReadOnlyList<string>? SelectedOptionCodes = null);

public sealed record SaveReviewerDraftRequest(
    string? OverallComments = null,
    IReadOnlyList<ConfirmationReviewResponseInput>? Responses = null);

public sealed record SubmitReviewerReviewRequest(
    string? OverallComments = null,
    IReadOnlyList<ConfirmationReviewResponseInput>? Responses = null);
