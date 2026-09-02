using FluentValidation;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Cities;

namespace HRMS.Application.Validators.Cities;

public class CityQueryValidator : AbstractValidator<CityQuery>
{
    public CityQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, PagedQuery.MaxPageSize);
        RuleFor(x => x.SortBy)
            .Must(sort => CityQuery.SortFields.Contains(sort!.ToLowerInvariant()))
            .When(x => !string.IsNullOrWhiteSpace(x.SortBy))
            .WithMessage($"SortBy must be one of: {string.Join(", ", CityQuery.SortFields)}");
    }
}
