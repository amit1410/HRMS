using FluentValidation;
using HRMS.Application.DTOs.Employees;

namespace HRMS.Application.Validators.Employees;

/// <summary>
/// Shape and cross-field validation for employee contact information. All properties are optional — the
/// business rules enforced here are format and length only; anything requiring stored-data checks is
/// handled by the service layer.
/// </summary>
public class EmployeeContactRequestValidator : AbstractValidator<EmployeeContactRequest>
{
    private const int PhoneMaxLength = 30;

    public EmployeeContactRequestValidator()
    {
        RuleFor(x => x.OfficialEmail)
            .EmailAddress().WithMessage("Official email must be a valid email address.")
            .When(x => !string.IsNullOrWhiteSpace(x.OfficialEmail));

        RuleFor(x => x.PersonalEmail)
            .EmailAddress().WithMessage("Personal email must be a valid email address.")
            .When(x => !string.IsNullOrWhiteSpace(x.PersonalEmail));

        RuleFor(x => x.AlternateEmail)
            .EmailAddress().WithMessage("Alternate email must be a valid email address.")
            .When(x => !string.IsNullOrWhiteSpace(x.AlternateEmail));

        RuleFor(x => x.OfficialPhone)
            .MaximumLength(PhoneMaxLength).WithMessage($"Official phone must not exceed {PhoneMaxLength} characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.OfficialPhone));

        RuleFor(x => x.PersonalPhone)
            .MaximumLength(PhoneMaxLength).WithMessage($"Personal phone must not exceed {PhoneMaxLength} characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.PersonalPhone));

        RuleFor(x => x.EmergencyNumber)
            .MaximumLength(PhoneMaxLength).WithMessage($"Emergency number must not exceed {PhoneMaxLength} characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.EmergencyNumber));
    }
}
