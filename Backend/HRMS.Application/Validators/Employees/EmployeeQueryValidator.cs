using FluentValidation;
using HRMS.Application.DTOs.Employees;
using HRMS.Application.Validators.Common;

namespace HRMS.Application.Validators.Employees;

public class EmployeeQueryValidator : PagedQueryValidator<EmployeeQuery>
{
    public EmployeeQueryValidator() : base(EmployeeQuery.SortFields)
    {
        RuleFor(x => x.Status)
            .IsInEnum().WithMessage("Status is not a recognized value.")
            .When(x => x.Status.HasValue);
    }
}
