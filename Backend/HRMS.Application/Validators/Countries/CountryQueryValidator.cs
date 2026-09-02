using FluentValidation;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Countries;

namespace HRMS.Application.Validators.Countries;

public class CountryQueryValidator : AbstractValidator<CountryQuery>
{
    public CountryQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, PagedQuery.MaxPageSize);
        RuleFor(x => x.SortBy)
            .Must(sort => CountryQuery.SortFields.Contains(sort!.ToLowerInvariant()))
            .When(x => !string.IsNullOrWhiteSpace(x.SortBy))
            .WithMessage($"SortBy must be one of: {string.Join(", ", CountryQuery.SortFields)}");
    }
}
