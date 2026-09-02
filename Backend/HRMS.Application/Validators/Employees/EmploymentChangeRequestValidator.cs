using FluentValidation;
using HRMS.Application.DTOs.Employees;
using HRMS.Domain.Enums;

namespace HRMS.Application.Validators.Employees;

/// <summary>
/// Shape and cross-field validation for employment change requests. Effective date must be today or
/// in the future; department and designation must be supplied. Any referential-integrity checks (that
/// the department or designation actually exists in this tenant) are handled by the service layer.
/// </summary>
public class EmploymentChangeRequestValidator : AbstractValidator<EmploymentChangeRequest>
{
    public EmploymentChangeRequestValidator()
    {
        RuleFor(x => x.EffectiveFrom)
            .NotEmpty().WithMessage("Effective date is required.")
            .GreaterThanOrEqualTo(Today)
                .WithMessage("Effective date must be today or in the future.");

        RuleFor(x => x.DepartmentId)
            .NotEmpty().WithMessage("Department is required.");

        RuleFor(x => x.DesignationId)
            .NotEmpty().WithMessage("Designation is required.");

        RuleFor(x => x.PositionChangeReasonId)
            .NotEmpty().WithMessage("Change reason is required.");

        RuleFor(x => x.ChangeReason)
            .IsInEnum().WithMessage("Change reason is invalid.")
            .NotEqual(EmploymentChangeReason.Unspecified).WithMessage("Change reason is required.");

        RuleFor(x => x.EmploymentType)
            .IsInEnum().WithMessage("Employment type is invalid.")
            .NotEqual(EmploymentType.Unspecified).WithMessage("Employment type is required.");

        RuleFor(x => x.EmploymentStatus)
            .IsInEnum().WithMessage("Employment status is invalid.");

        RuleFor(x => x.BusinessRole)
            .MaximumLength(200).WithMessage("Business role must not exceed 200 characters.");

        RuleFor(x => x.GradeLevel)
            .MaximumLength(50).WithMessage("Grade level must not exceed 50 characters.");

        RuleFor(x => x.CareerGroup)
            .MaximumLength(100).WithMessage("Career group must not exceed 100 characters.");

        RuleFor(x => x.ChangeReasonDescription)
            .MaximumLength(500).WithMessage("Change reason description must not exceed 500 characters.");
    }

    /// <summary>
    /// Today in UTC, read per validation rather than captured once, so a long-lived validator instance
    /// cannot go stale across a date boundary.
    /// </summary>
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
}
