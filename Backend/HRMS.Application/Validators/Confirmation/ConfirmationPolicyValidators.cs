using FluentValidation;
using HRMS.Application.DTOs.Confirmation;
using HRMS.Application.Validators.Common;

namespace HRMS.Application.Validators.Confirmation;

/// <summary>
/// Shape validation for a confirmation-policy write. Code/name uniqueness is a per-tenant stored-data
/// question and is checked in the service, not here.
/// </summary>
public sealed class ConfirmationPolicyRequestValidator : AbstractValidator<ConfirmationPolicyRequest>
{
    public ConfirmationPolicyRequestValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Policy code is required.")
            .MaximumLength(40).WithMessage("Policy code must not exceed 40 characters.")
            .Matches(CodeFormats.Pattern).WithMessage(CodeFormats.Message);

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Policy name is required.")
            .MaximumLength(150).WithMessage("Policy name must not exceed 150 characters.");

        RuleFor(x => x.Description)
            .MaximumLength(1000).WithMessage("Description must not exceed 1000 characters.");
    }
}

public sealed class ConfirmationProbationConfigRequestValidator : AbstractValidator<ConfirmationProbationConfigRequest>
{
    public ConfirmationProbationConfigRequestValidator()
    {
        RuleFor(x => x.ProbationDuration)
            .GreaterThan(0).WithMessage("Probation duration must be greater than zero.")
            .LessThanOrEqualTo(120).WithMessage("Probation duration is unrealistically large.");

        RuleFor(x => x.TriggerDaysBeforeEnd)
            .InclusiveBetween(0, 365).WithMessage("Trigger days before end must be between 0 and 365.");

        RuleFor(x => x.MaxExtensions)
            .InclusiveBetween(0, 10).WithMessage("Max extensions must be between 0 and 10.");

        RuleFor(x => x.ExtensionDuration)
            .GreaterThan(0).WithMessage("Extension duration must be greater than zero when extensions are allowed.")
            .When(x => x.MaxExtensions > 0);

        RuleFor(x => x.ConfirmRatingThreshold)
            .InclusiveBetween(0m, 100m).WithMessage("Rating threshold must be between 0 and 100.")
            .When(x => x.ConfirmRatingThreshold.HasValue);
    }
}

public sealed class ConfirmationPolicyVersionRequestValidator : AbstractValidator<ConfirmationPolicyVersionRequest>
{
    public ConfirmationPolicyVersionRequestValidator()
    {
        RuleFor(x => x.Priority).InclusiveBetween(0, 1000).WithMessage("Priority must be between 0 and 1000.");

        RuleFor(x => x.EffectiveTo)
            .Must((request, to) => to is null || to >= request.EffectiveFrom)
            .WithMessage("Effective-to cannot be before effective-from.");

        RuleFor(x => x.Probation).NotNull().WithMessage("Probation configuration is required.")
            .SetValidator(new ConfirmationProbationConfigRequestValidator()!);
    }
}
