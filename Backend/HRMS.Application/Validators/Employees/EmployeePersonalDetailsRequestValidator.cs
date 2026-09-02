using FluentValidation;
using HRMS.Application.DTOs.Employees;
using HRMS.Domain.Enums;

namespace HRMS.Application.Validators.Employees;

/// <summary>
/// Shape and cross-field validation for the Employee → Personal Details section.
/// <para>
/// Everything here can be decided from the payload alone; anything needing stored data — that a birth
/// country/state/city exists and cascade correctly — is checked by the service, where the tenant (and the
/// record being edited) are known. The reference-validation helper is shared with
/// <see cref="EmployeeRequestValidator"/> so the two write paths cannot disagree about a field they both own.
/// </summary>
public class EmployeePersonalDetailsRequestValidator : AbstractValidator<EmployeePersonalDetailsRequest>
{
    /// <summary>Sanity floor for a working age. A data-entry guard, not a statement of employment law.</summary>
    private const int MinimumAgeYears = 14;

    /// <summary>How far ahead a start date may be set, so onboarding can be recorded before day one.</summary>
    private const int MaxFutureJoiningYears = 1;

    public EmployeePersonalDetailsRequestValidator()
    {
        RuleFor(x => x.FirstName)
            .NotEmpty().WithMessage("First name is required.")
            .MaximumLength(100).WithMessage("First name must not exceed 100 characters.");

        RuleFor(x => x.LastName)
            .NotEmpty().WithMessage("Last name is required.")
            .MaximumLength(100).WithMessage("Last name must not exceed 100 characters.");

        RuleFor(x => x.Gender)
            .IsInEnum().WithMessage("Gender is not a recognized value.");

        RuleFor(x => x.DateOfJoining)
            .Cascade(CascadeMode.Stop)
            .NotEqual(default(DateOnly)).WithMessage("Date of joining is required.")
            .Must(date => date <= Today.AddYears(MaxFutureJoiningYears))
                .WithMessage($"Date of joining may not be more than {MaxFutureJoiningYears} year(s) in the future.")
            .Must(date => date >= Today.AddYears(-100))
                .WithMessage("Date of joining is too far in the past to be plausible.");

        RuleFor(x => x.DateOfBirth)
            .Must(date => date!.Value < Today).WithMessage("Date of birth must be in the past.")
            .Must(date => date!.Value >= Today.AddYears(-120)).WithMessage("Date of birth is not plausible.")
            .When(x => x.DateOfBirth.HasValue);

        // Age is checked against the start date rather than today, so an old record does not become
        // invalid — and cannot be saved valid — merely because time has passed.
        RuleFor(x => x.DateOfBirth)
            .Must((request, dateOfBirth) => dateOfBirth!.Value.AddYears(MinimumAgeYears) <= request.DateOfJoining)
            .WithMessage($"An employee must be at least {MinimumAgeYears} years old on their date of joining.")
            .When(x => x.DateOfBirth.HasValue);

        RuleFor(x => x.AadhaarNumber)
            .Matches(@"^\d{12}$").WithMessage("Aadhaar number must be exactly 12 digits.")
            .When(x => !string.IsNullOrWhiteSpace(x.AadhaarNumber));

        RuleFor(x => x.PanNumber)
            .Matches(@"^[A-Z]{5}\d{4}[A-Z]$").WithMessage("PAN must be in the format ABCDE1234F (5 letters, 4 digits, 1 letter).")
            .When(x => !string.IsNullOrWhiteSpace(x.PanNumber));

        RuleFor(x => x.UanNumber)
            .Matches(@"^\d{12}$").WithMessage("UAN must be exactly 12 digits.")
            .When(x => !string.IsNullOrWhiteSpace(x.UanNumber));

        // ESIC Number is mandatory when ESIC Applicable is set to true.
        RuleFor(x => x.EsicNumber)
            .NotEmpty().WithMessage("ESIC Number is required when ESIC is applicable.")
            .When(x => x.EsicApplicable);

        RuleFor(x => x.EsicNumber)
            .MaximumLength(50).WithMessage("ESIC Number must not exceed 50 characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.EsicNumber));

        // Birth location cascading validation (shape only — referential integrity checked by the service).
        RuleFor(x => x.BirthStateId)
            .Null().WithMessage("Birth state requires a birth country to be selected.")
            .When(x => x.BirthStateId.HasValue && !x.BirthCountryId.HasValue);

        RuleFor(x => x.BirthCityId)
            .Null().WithMessage("Birth city requires a birth state to be selected.")
            .When(x => x.BirthCityId.HasValue && !x.BirthStateId.HasValue);
    }

    /// <summary>
    /// Today in UTC, read per validation rather than captured once, so a long-lived validator instance
    /// cannot go stale across a date boundary.
    /// </summary>
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
}
