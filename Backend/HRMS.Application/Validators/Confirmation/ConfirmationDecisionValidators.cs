using FluentValidation;
using HRMS.Application.DTOs.Confirmation;
using HRMS.Domain.Enums;

namespace HRMS.Application.Validators.Confirmation;

/// <summary>Shape validation for a final confirmation decision. Readiness, authorization, the permitted-outcome
/// set and all workflow rules are enforced in the decision service, which is the only place the case snapshot,
/// submitted reviews and configured rules are known.</summary>
public sealed class SubmitConfirmationDecisionRequestValidator : AbstractValidator<SubmitConfirmationDecisionRequest>
{
    public SubmitConfirmationDecisionRequestValidator()
    {
        RuleFor(x => x.Outcome)
            .Must(o => o != ConfirmationOutcome.Pending)
            .WithMessage("A decision outcome is required.")
            .IsInEnum().WithMessage("Unknown decision outcome.");
        RuleFor(x => x.DecisionReason).MaximumLength(4000).WithMessage("Decision comments must not exceed 4000 characters.");
    }
}
