using FluentValidation;
using HRMS.Application.DTOs.Employees;
using HRMS.Application.Validators.Common;
using HRMS.Domain.Enums;

namespace HRMS.Application.Validators.Employees;

/// <summary>
/// Shape and cross-field validation for an employee write. Everything here can be decided from the payload
/// alone; anything needing stored data — that the department exists in this tenant, that the code is not
/// already taken, that the manager is not the employee themselves — is checked by the service, where the
/// tenant and the record being edited are known.
/// </summary>
public class EmployeeRequestValidator : AbstractValidator<EmployeeRequest>
{
    /// <summary>Sanity floor for a working age. A data-entry guard, not a statement of employment law.</summary>
    private const int MinimumAgeYears = 14;

    /// <summary>How far ahead a start date may be set, so onboarding can be recorded before day one.</summary>
    private const int MaxFutureJoiningYears = 1;

    public EmployeeRequestValidator()
    {
        RuleFor(x => x.EmployeeCode)
            .NotEmpty().WithMessage("Employee code is required.")
            .MaximumLength(20).WithMessage("Employee code must not exceed 20 characters.")
            .Matches(CodeFormats.Pattern).WithMessage(CodeFormats.Message);

        RuleFor(x => x.FirstName)
            .NotEmpty().WithMessage("First name is required.")
            .MaximumLength(100).WithMessage("First name must not exceed 100 characters.");

        RuleFor(x => x.LastName)
            .NotEmpty().WithMessage("Last name is required.")
            .MaximumLength(100).WithMessage("Last name must not exceed 100 characters.");

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .MaximumLength(256).WithMessage("Email must not exceed 256 characters.")
            .EmailAddress().WithMessage("Email must be a valid email address.");

        RuleFor(x => x.Phone)
            .MaximumLength(30).WithMessage("Phone must not exceed 30 characters.")
            .Matches(@"^[0-9+()\-.\s]+$").WithMessage("Phone may contain only digits and the characters + - ( ) . and spaces.")
            .When(x => !string.IsNullOrWhiteSpace(x.Phone));

        RuleFor(x => x.Address)
            .MaximumLength(500).WithMessage("Address must not exceed 500 characters.");

        RuleFor(x => x.Gender)
            .IsInEnum().WithMessage("Gender is not a recognized value.");

        RuleFor(x => x.Status)
            .IsInEnum().WithMessage("Status is not a recognized value.");

        // Department and designation are captured by the Employment/Position section, which is built later,
        // so they are optional here — an employee can be created from Personal Details alone.
        RuleFor(x => x.DepartmentId)
            .NotEqual(Guid.Empty).WithMessage("Department is not a valid selection.")
            .When(x => x.DepartmentId.HasValue);

        RuleFor(x => x.DesignationId)
            .NotEqual(Guid.Empty).WithMessage("Designation is not a valid selection.")
            .When(x => x.DesignationId.HasValue);

        RuleFor(x => x.ReportingManagerId)
            .NotEqual(Guid.Empty).WithMessage("Reporting manager is not a valid employee.")
            .When(x => x.ReportingManagerId.HasValue);

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
            .When(x => x.DateOfBirth.HasValue && x.DateOfJoining != default);

        // Leaving date and status have to agree: an active employee has not left, and one who has left
        // needs a last working day on or after their first.
        RuleFor(x => x.DateOfLeaving)
            .Null().WithMessage("Date of leaving must be empty for an active employee.")
            .When(x => x.Status == EmployeeStatus.Active);

        RuleFor(x => x.DateOfLeaving)
            .NotNull().WithMessage("Date of leaving is required once an employee is no longer active.")
            .When(x => x.Status != EmployeeStatus.Active);

        RuleFor(x => x.DateOfLeaving)
            .Must((request, dateOfLeaving) => dateOfLeaving!.Value >= request.DateOfJoining)
            .WithMessage("Date of leaving cannot be before the date of joining.")
            .When(x => x.DateOfLeaving.HasValue && x.DateOfJoining != default);

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

        // ESIC Number validation format (when provided).
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
