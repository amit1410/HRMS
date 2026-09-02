using FluentValidation;
using HRMS.Application.DTOs.Countries;
using HRMS.Application.Validators.Common;

namespace HRMS.Application.Validators.Countries;

public class CountryRequestValidator : AbstractValidator<CountryRequest>
{
    public CountryRequestValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Country code is required.")
            .MaximumLength(10).WithMessage("Country code must not exceed 10 characters.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Country name is required.")
            .MaximumLength(100).WithMessage("Country name must not exceed 100 characters.");
    }
}
