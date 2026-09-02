using FluentValidation;
using HRMS.Application.DTOs.Employees;
using HRMS.Domain.Enums;

namespace HRMS.Application.Validators.Employees;

public sealed class EmployeeCodeConfigurationRequestValidator : AbstractValidator<EmployeeCodeConfigurationRequest>
{
    public EmployeeCodeConfigurationRequestValidator()
    {
        RuleFor(x => x.Prefix).NotEmpty().MaximumLength(10).Matches("^[A-Za-z0-9_-]+$");
        RuleFor(x => x.NextNumber).GreaterThanOrEqualTo(1);
        RuleFor(x => x.Padding).InclusiveBetween(0, 10);
        RuleFor(x => x.Separator).NotNull().Must(value => value is "" or "-" or "/" or "." or "_")
            .WithMessage("Separator must be blank, -, /, ., or _.");
        RuleFor(x => x.GenerationMethod)
            .NotNull()
            .When(x => x.AssignmentMode == EmployeeCodeAssignmentMode.Auto)
            .WithMessage("Generation method is required for Auto assignment.");
        RuleFor(x => x.GenerationMethod)
            .Null()
            .When(x => x.AssignmentMode == EmployeeCodeAssignmentMode.Manual)
            .WithMessage("Generation method must be empty for Manual assignment.");
        RuleFor(x => x).Must(x => !x.EffectiveTo.HasValue || x.EffectiveTo.Value >= x.EffectiveFrom)
            .WithMessage("Effective end date cannot be before the start date.");
    }
}
