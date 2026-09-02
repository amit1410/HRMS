using FluentValidation;
using HRMS.Application.Common;
using HRMS.Application.DTOs.States;

namespace HRMS.Application.Validators.States;

public class StateQueryValidator : AbstractValidator<StateQuery>
{
    public StateQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, PagedQuery.MaxPageSize);
        RuleFor(x => x.SortBy)
            .Must(sort => StateQuery.SortFields.Contains(sort!.ToLowerInvariant()))
            .When(x => !string.IsNullOrWhiteSpace(x.SortBy))
            .WithMessage($"SortBy must be one of: {string.Join(", ", StateQuery.SortFields)}");
    }
}
