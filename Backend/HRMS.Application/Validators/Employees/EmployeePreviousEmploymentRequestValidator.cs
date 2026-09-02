using FluentValidation;
using HRMS.Application.DTOs.Employees;

namespace HRMS.Application.Validators.Employees;

/// <summary>
/// Shape and cross-field validation for employee previous employment information. Company is required;
/// designation is optional with a length constraint applied when present.
/// </summary>
public class EmployeePreviousEmploymentRequestValidator : AbstractValidator<EmployeePreviousEmploymentRequest>
{
    private const int TextMaxLength = 200;

    public EmployeePreviousEmploymentRequestValidator()
    {
        RuleFor(x => x.Company)
            .NotEmpty().WithMessage("Company is required.")
            .MaximumLength(TextMaxLength).WithMessage($"Company must not exceed {TextMaxLength} characters.");

        RuleFor(x => x.Designation)
            .MaximumLength(TextMaxLength).WithMessage($"Designation must not exceed {TextMaxLength} characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.Designation));
    }
}
