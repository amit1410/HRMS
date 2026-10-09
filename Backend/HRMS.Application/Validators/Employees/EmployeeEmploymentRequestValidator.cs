using FluentValidation;
using HRMS.Application.DTOs.Employees;
using HRMS.Domain.Enums;

namespace HRMS.Application.Validators.Employees;

/// <summary>Shape and date-order validation for joining and contractual employment information.</summary>
public class EmployeeEmploymentRequestValidator : AbstractValidator<EmployeeEmploymentRequest>
{
    private static readonly string[] NoticeUnits = ["Days", "Months"];

    public EmployeeEmploymentRequestValidator()
    {
        // Date of joining is owned by personal details (Employee.DateOfJoining); it is no longer
        // part of this request. The confirmation-date vs date-of-joining ordering is enforced in
        // EmployeeEmploymentService against the employee's authoritative date of joining.
        RuleFor(x => x.JobStatus)
            .MaximumLength(100).WithMessage("Job status must not exceed 100 characters.");

        RuleFor(x => x.NoticePeriod)
            .GreaterThan(0).WithMessage("Notice period must be greater than zero.")
            .When(x => x.NoticePeriod.HasValue);

        RuleFor(x => x.NoticePeriodUnit)
            .Must(unit => unit is not null && NoticeUnits.Contains(unit, StringComparer.OrdinalIgnoreCase))
                .WithMessage("Notice period unit must be Days or Months.")
            .When(x => x.NoticePeriod.HasValue || !string.IsNullOrWhiteSpace(x.NoticePeriodUnit));

        RuleFor(x => x.NoticeStatus)
            .IsInEnum().WithMessage("Notice status is invalid.");

        RuleFor(x => x.NoticeStartDate)
            .NotNull().WithMessage("Notice start date is required while the employee is serving notice.")
            .When(x => x.NoticeStatus == NoticePeriodStatus.Active);

        RuleFor(x => x.NoticeEndDate)
            .GreaterThanOrEqualTo(x => x.NoticeStartDate)
            .WithMessage("Notice end date cannot be before the notice start date.")
            .When(x => x.NoticeEndDate.HasValue && x.NoticeStartDate.HasValue);
    }
}
