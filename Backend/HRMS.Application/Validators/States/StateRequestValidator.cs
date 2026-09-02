using FluentValidation;
using HRMS.Application.DTOs.States;

namespace HRMS.Application.Validators.States;

public class StateRequestValidator : AbstractValidator<StateRequest>
{
    public StateRequestValidator()
    {
        RuleFor(x => x.CountryId)
            .NotEmpty().WithMessage("Country is required.");

        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("State code is required.")
            .MaximumLength(10).WithMessage("State code must not exceed 10 characters.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("State name is required.")
            .MaximumLength(100).WithMessage("State name must not exceed 100 characters.");
    }
}
