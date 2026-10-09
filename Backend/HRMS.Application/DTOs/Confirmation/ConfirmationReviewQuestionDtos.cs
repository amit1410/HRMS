using HRMS.Application.Common;
using HRMS.Domain.Enums;

namespace HRMS.Application.DTOs.Confirmation;

// --- Review question ---

public sealed record ConfirmationReviewQuestionDto(
    Guid Id,
    string Code,
    string QuestionText,
    string? HelpText,
    ReviewQuestionType QuestionType,
    bool IsMandatory,
    bool IsActive,
    int DisplayOrder,
    DateOnly? EffectiveFrom,
    DateOnly? EffectiveTo,
    Guid? ConfirmationRatingScaleId,
    string? RatingScaleName,
    int OptionCount,
    int ApplicabilitySetCount,
    DateTime CreatedDate,
    DateTime? ModifiedDate);

public sealed record ConfirmationReviewQuestionOptionDto(
    Guid Id,
    string Code,
    string Label,
    int DisplayOrder,
    bool IsActive);

public sealed record ConfirmationReviewQuestionApplicabilityDto(
    Guid Id,
    Guid? ConfirmationPolicyId,
    Gender? Gender,
    Guid? HoldingCompanyId,
    Guid? LobId,
    Guid? OrganisationId,
    Guid? DepartmentId,
    Guid? SubDepartmentId,
    Guid? SectionId,
    Guid? SubSectionId,
    Guid? FunctionId,
    Guid? SubFunctionId,
    Guid? GradeId,
    Guid? DesignationId,
    Guid? EmployeeTypeId,
    Guid? CountryLocationId,
    Guid? WorkLocationId,
    Guid? CostCenterId);

public sealed record ConfirmationReviewQuestionDetailDto(
    ConfirmationReviewQuestionDto Question,
    IReadOnlyList<ConfirmationReviewQuestionOptionDto> Options,
    IReadOnlyList<ConfirmationReviewQuestionApplicabilityDto> Applicability);

public sealed class ConfirmationReviewQuestionQuery : PagedQuery
{
    public bool? IsActive { get; set; }
    public ReviewQuestionType? QuestionType { get; set; }

    /// <summary>Supported sort fields for this endpoint (validated against this whitelist).</summary>
    public static readonly IReadOnlyList<string> SortFields = new[] { "code", "displayorder", "questiontype", "isactive", "createddate" };
}

public sealed record ConfirmationReviewQuestionOptionRequest(
    string Code,
    string Label,
    int DisplayOrder = 0,
    bool IsActive = true);

public sealed record ConfirmationReviewQuestionApplicabilityRequest(
    Guid? ConfirmationPolicyId = null,
    Gender? Gender = null,
    Guid? HoldingCompanyId = null,
    Guid? LobId = null,
    Guid? OrganisationId = null,
    Guid? DepartmentId = null,
    Guid? SubDepartmentId = null,
    Guid? SectionId = null,
    Guid? SubSectionId = null,
    Guid? FunctionId = null,
    Guid? SubFunctionId = null,
    Guid? GradeId = null,
    Guid? DesignationId = null,
    Guid? EmployeeTypeId = null,
    Guid? CountryLocationId = null,
    Guid? WorkLocationId = null,
    Guid? CostCenterId = null);

public sealed record ConfirmationReviewQuestionRequest(
    string Code,
    string QuestionText,
    string? HelpText,
    ReviewQuestionType QuestionType,
    bool IsMandatory = false,
    bool IsActive = true,
    int DisplayOrder = 0,
    DateOnly? EffectiveFrom = null,
    DateOnly? EffectiveTo = null,
    Guid? ConfirmationRatingScaleId = null,
    IReadOnlyList<ConfirmationReviewQuestionOptionRequest>? Options = null,
    IReadOnlyList<ConfirmationReviewQuestionApplicabilityRequest>? Applicability = null);
