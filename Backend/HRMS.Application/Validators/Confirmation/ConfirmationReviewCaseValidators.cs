using FluentValidation;
using HRMS.Application.DTOs.Confirmation;

namespace HRMS.Application.Validators.Confirmation;

/// <summary>Shape validation for creating/initializing a review case. Eligibility, policy resolution and
/// duplicate prevention are per-tenant stored-data questions handled in the service.</summary>
public sealed class CreateConfirmationReviewCaseRequestValidator : AbstractValidator<CreateConfirmationReviewCaseRequest>
{
    public CreateConfirmationReviewCaseRequestValidator()
    {
        RuleFor(x => x.EmployeeId).NotEmpty().WithMessage("An employee is required.");
    }
}

/// <summary>Shape validation for a reviewer draft. Response correctness is validated against the case snapshot
/// in the service, which is the only place the snapshot is known.</summary>
public sealed class SaveReviewerDraftRequestValidator : AbstractValidator<SaveReviewerDraftRequest>
{
    public SaveReviewerDraftRequestValidator()
    {
        RuleFor(x => x.OverallComments).MaximumLength(4000).WithMessage("Comments must not exceed 4000 characters.");
        RuleForEach(x => x.Responses).SetValidator(new ConfirmationReviewResponseInputValidator());
    }
}

public sealed class SubmitReviewerReviewRequestValidator : AbstractValidator<SubmitReviewerReviewRequest>
{
    public SubmitReviewerReviewRequestValidator()
    {
        RuleFor(x => x.OverallComments).MaximumLength(4000).WithMessage("Comments must not exceed 4000 characters.");
        RuleForEach(x => x.Responses).SetValidator(new ConfirmationReviewResponseInputValidator());
    }
}

public sealed class ConfirmationReviewResponseInputValidator : AbstractValidator<ConfirmationReviewResponseInput>
{
    public ConfirmationReviewResponseInputValidator()
    {
        RuleFor(x => x.ConfirmationReviewCaseQuestionId).NotEmpty().WithMessage("A question is required for each response.");
        RuleFor(x => x.TextValue).MaximumLength(4000).WithMessage("Text answer must not exceed 4000 characters.");
    }
}
