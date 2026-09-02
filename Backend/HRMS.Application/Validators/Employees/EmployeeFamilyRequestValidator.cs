using FluentValidation;
using HRMS.Application.DTOs.Employees;
using HRMS.Domain.Enums;

namespace HRMS.Application.Validators.Employees;

/// <summary>
/// Shape and cross-field validation for employee family member information. First name, last name
/// and relationship are required; gender is validated as a recognised enum value.
/// </summary>
public class EmployeeFamilyRequestValidator : AbstractValidator<EmployeeFamilyRequest>
{
    private const int NameMaxLength = 100;
    private const int RelationshipMaxLength = 50;

    public EmployeeFamilyRequestValidator()
    {
        RuleFor(x => x.FirstName)
            .NotEmpty().WithMessage("First name is required.")
            .MaximumLength(NameMaxLength).WithMessage($"First name must not exceed {NameMaxLength} characters.");

        RuleFor(x => x.LastName)
            .NotEmpty().WithMessage("Last name is required.")
            .MaximumLength(NameMaxLength).WithMessage($"Last name must not exceed {NameMaxLength} characters.");

        RuleFor(x => x.Relationship)
            .NotEmpty().WithMessage("Relationship is required.")
            .MaximumLength(RelationshipMaxLength).WithMessage($"Relationship must not exceed {RelationshipMaxLength} characters.");

        RuleFor(x => x.Gender)
            .IsInEnum().WithMessage("Gender is not a recognized value.");

        RuleFor(x => x.NomineePercentage)
            .InclusiveBetween(0.01m, 100m)
            .WithMessage("Nominee percentage must be between 0.01 and 100.")
            .When(x => x.IsNominee && x.NomineePercentage.HasValue);
    }
}
