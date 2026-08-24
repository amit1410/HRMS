using FluentValidation;
using HRMS.Application.DTOs.Departments;
using HRMS.Application.Validators.Common;

namespace HRMS.Application.Validators.Departments;

/// <summary>
/// Shape validation for a department write. Uniqueness of code and name is not checked here: it is a
/// question about stored data within one tenant, so it belongs where the tenant is known — the service.
/// </summary>
public class DepartmentRequestValidator : AbstractValidator<DepartmentRequest>
{
    public DepartmentRequestValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Department code is required.")
            .MaximumLength(20).WithMessage("Department code must not exceed 20 characters.")
            .Matches(CodeFormats.Pattern).WithMessage(CodeFormats.Message);

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Department name is required.")
            .MaximumLength(100).WithMessage("Department name must not exceed 100 characters.");

        RuleFor(x => x.Description)
            .MaximumLength(500).WithMessage("Description must not exceed 500 characters.");
    }
}
