using FluentValidation;
using HRMS.Application.DTOs.Employees;

namespace HRMS.Application.Validators.Employees;

/// <summary>
/// Shape and cross-field validation for employee education information. Qualification is required;
/// institution and university are optional with length constraints applied when present.
/// </summary>
public class EmployeeEducationRequestValidator : AbstractValidator<EmployeeEducationRequest>
{
    private const int QualificationMaxLength = 200;
    private const int InstitutionMaxLength = 200;

    public EmployeeEducationRequestValidator()
    {
        RuleFor(x => x.Qualification)
            .NotEmpty().WithMessage("Qualification is required.")
            .MaximumLength(QualificationMaxLength).WithMessage($"Qualification must not exceed {QualificationMaxLength} characters.");

        RuleFor(x => x.Institute)
            .MaximumLength(InstitutionMaxLength).WithMessage($"Institute must not exceed {InstitutionMaxLength} characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.Institute));

        RuleFor(x => x.University)
            .MaximumLength(InstitutionMaxLength).WithMessage($"University must not exceed {InstitutionMaxLength} characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.University));
    }
}
