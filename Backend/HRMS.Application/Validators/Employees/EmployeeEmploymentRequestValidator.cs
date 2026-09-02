using FluentValidation;
using HRMS.Application.DTOs.Employees;

namespace HRMS.Application.Validators.Employees;

/// <summary>Shape and date-order validation for joining and contractual employment information.</summary>
public class EmployeeEmploymentRequestValidator : AbstractValidator<EmployeeEmploymentRequest>
{
    private static readonly string[] ProbationUnits = ["Days", "Months", "Years"];
    private static readonly string[] NoticeUnits = ["Days", "Months"];

    public EmployeeEmploymentRequestValidator()
    {
        RuleFor(x => x.FirstHiredDate)
            .NotEmpty().WithMessage("First hired date is required.");

        RuleFor(x => x.DateOfJoining)
            .NotEmpty().WithMessage("Date of joining is required.")
            .GreaterThanOrEqualTo(x => x.FirstHiredDate)
                .WithMessage("Date of joining cannot be before the first hired date.");

        RuleFor(x => x.GroupDateOfJoining)
            .LessThanOrEqualTo(x => x.DateOfJoining)
                .WithMessage("Group date of joining cannot be after the date of joining.")
            .When(x => x.GroupDateOfJoining.HasValue);

        RuleFor(x => x.ConfirmationDate)
            .GreaterThanOrEqualTo(x => x.DateOfJoining)
                .WithMessage("Confirmation date cannot be before the date of joining.")
            .When(x => x.ConfirmationDate.HasValue);

        RuleFor(x => x.JobStatus)
            .MaximumLength(100).WithMessage("Job status must not exceed 100 characters.");

        RuleFor(x => x.ProbationPeriod)
            .GreaterThan(0).WithMessage("Probation period must be greater than zero.")
            .When(x => x.ProbationPeriod.HasValue);

        RuleFor(x => x.ProbationPeriodUnit)
            .Must(unit => unit is not null && ProbationUnits.Contains(unit, StringComparer.OrdinalIgnoreCase))
                .WithMessage("Probation period unit must be Days, Months, or Years.")
            .When(x => x.ProbationPeriod.HasValue || !string.IsNullOrWhiteSpace(x.ProbationPeriodUnit));

        RuleFor(x => x.NoticePeriod)
            .GreaterThan(0).WithMessage("Notice period must be greater than zero.")
            .When(x => x.NoticePeriod.HasValue);

        RuleFor(x => x.NoticePeriodUnit)
            .Must(unit => unit is not null && NoticeUnits.Contains(unit, StringComparer.OrdinalIgnoreCase))
                .WithMessage("Notice period unit must be Days or Months.")
            .When(x => x.NoticePeriod.HasValue || !string.IsNullOrWhiteSpace(x.NoticePeriodUnit));
    }
}
