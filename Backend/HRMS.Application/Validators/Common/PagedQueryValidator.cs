using FluentValidation;
using HRMS.Application.Common;

namespace HRMS.Application.Validators.Common;

/// <summary>
/// Shared rules for every paged list endpoint: sane page bounds, a bounded search term, and a sort field
/// drawn from the endpoint's own whitelist.
/// <para>
/// An unsupported sort field is rejected rather than ignored. Silently falling back to a default order
/// would let a client believe a report was sorted by a column that was never applied, which is worse than
/// an error message naming the fields that do work.
/// </para>
/// </summary>
public abstract class PagedQueryValidator<TQuery> : AbstractValidator<TQuery>
    where TQuery : PagedQuery
{
    protected PagedQueryValidator(IReadOnlyList<string> sortFields)
    {
        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(1).WithMessage("Page must be 1 or greater.");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, PagedQuery.MaxPageSize)
            .WithMessage($"Page size must be between 1 and {PagedQuery.MaxPageSize}.");

        RuleFor(x => x.Search)
            .MaximumLength(100).WithMessage("Search text must not exceed 100 characters.");

        RuleFor(x => x.SortBy)
            .Must(value => string.IsNullOrWhiteSpace(value)
                           || sortFields.Any(field => string.Equals(field, value, StringComparison.OrdinalIgnoreCase)))
            .WithMessage($"Sort field must be one of: {string.Join(", ", sortFields)}.");
    }
}
