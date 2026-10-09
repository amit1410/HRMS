using FluentValidation;
using HRMS.Application.DTOs.Confirmation;
using HRMS.Application.Validators.Common;

namespace HRMS.Application.Validators.Confirmation;

/// <summary>
/// Shape validation for a rating-scale write. Code uniqueness and cross-field value checks (value within
/// bounds, duplicates) are per-tenant stored-data questions handled in the service.
/// </summary>
public sealed class ConfirmationRatingScaleRequestValidator : AbstractValidator<ConfirmationRatingScaleRequest>
{
    public ConfirmationRatingScaleRequestValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Rating scale code is required.")
            .MaximumLength(40).WithMessage("Rating scale code must not exceed 40 characters.")
            .Matches(CodeFormats.Pattern).WithMessage(CodeFormats.Message);

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Rating scale name is required.")
            .MaximumLength(150).WithMessage("Rating scale name must not exceed 150 characters.");

        RuleFor(x => x.Description).MaximumLength(1000).WithMessage("Description must not exceed 1000 characters.");

        RuleFor(x => x.MinRating).InclusiveBetween(0, 100).WithMessage("Minimum rating must be between 0 and 100.");
        RuleFor(x => x.MaxRating).InclusiveBetween(1, 100).WithMessage("Maximum rating must be between 1 and 100.");
        RuleFor(x => x.DisplayOrder).GreaterThanOrEqualTo(0).WithMessage("Display order cannot be negative.");

        RuleForEach(x => x.Values).SetValidator(new ConfirmationRatingScaleValueRequestValidator());
    }
}

public sealed class ConfirmationRatingScaleValueRequestValidator : AbstractValidator<ConfirmationRatingScaleValueRequest>
{
    public ConfirmationRatingScaleValueRequestValidator()
    {
        RuleFor(x => x.Label)
            .NotEmpty().WithMessage("Rating label is required.")
            .MaximumLength(150).WithMessage("Rating label must not exceed 150 characters.");
        RuleFor(x => x.Description).MaximumLength(500).WithMessage("Rating description must not exceed 500 characters.");
        RuleFor(x => x.DisplayOrder).GreaterThanOrEqualTo(0).WithMessage("Display order cannot be negative.");
    }
}

/// <summary>
/// Shape validation for a review-question write. Type-specific rules (rating scale required for Rating,
/// options required for Dropdown/MultiSelect, referential existence) are per-tenant stored-data questions
/// handled in the service.
/// </summary>
public sealed class ConfirmationReviewQuestionRequestValidator : AbstractValidator<ConfirmationReviewQuestionRequest>
{
    public ConfirmationReviewQuestionRequestValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Question code is required.")
            .MaximumLength(40).WithMessage("Question code must not exceed 40 characters.")
            .Matches(CodeFormats.Pattern).WithMessage(CodeFormats.Message);

        RuleFor(x => x.QuestionText)
            .NotEmpty().WithMessage("Question text is required.")
            .MaximumLength(500).WithMessage("Question text must not exceed 500 characters.");

        RuleFor(x => x.HelpText).MaximumLength(1000).WithMessage("Help text must not exceed 1000 characters.");
        RuleFor(x => x.QuestionType).IsInEnum().WithMessage("A valid question type is required.");
        RuleFor(x => x.DisplayOrder).GreaterThanOrEqualTo(0).WithMessage("Display order cannot be negative.");

        RuleForEach(x => x.Options).SetValidator(new ConfirmationReviewQuestionOptionRequestValidator());
    }
}

public sealed class ConfirmationReviewQuestionOptionRequestValidator : AbstractValidator<ConfirmationReviewQuestionOptionRequest>
{
    public ConfirmationReviewQuestionOptionRequestValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Option code is required.")
            .MaximumLength(60).WithMessage("Option code must not exceed 60 characters.");
        RuleFor(x => x.Label)
            .NotEmpty().WithMessage("Option label is required.")
            .MaximumLength(200).WithMessage("Option label must not exceed 200 characters.");
        RuleFor(x => x.DisplayOrder).GreaterThanOrEqualTo(0).WithMessage("Display order cannot be negative.");
    }
}
