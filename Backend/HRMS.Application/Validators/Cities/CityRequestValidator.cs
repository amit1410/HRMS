using FluentValidation;
using HRMS.Application.DTOs.Cities;

namespace HRMS.Application.Validators.Cities;

public class CityRequestValidator : AbstractValidator<CityRequest>
{
    public CityRequestValidator()
    {
        RuleFor(x => x.StateId)
            .NotEmpty().WithMessage("State is required.");

        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("City code is required.")
            .MaximumLength(10).WithMessage("City code must not exceed 10 characters.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("City name is required.")
            .MaximumLength(100).WithMessage("City name must not exceed 100 characters.");
    }
}
