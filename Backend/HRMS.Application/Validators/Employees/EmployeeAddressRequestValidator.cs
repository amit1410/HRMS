using FluentValidation;
using HRMS.Application.DTOs.Employees;
using HRMS.Domain.Enums;

namespace HRMS.Application.Validators.Employees;

/// <summary>
/// Shape and cross-field validation for employee address information. Address type is required; all
/// other properties are optional with length constraints applied when a value is present.
/// </summary>
public class EmployeeAddressRequestValidator : AbstractValidator<EmployeeAddressRequest>
{
    private const int LocationMaxLength = 100;
    private const int ZipCodeMaxLength = 20;
    private const int AddressLineMaxLength = 500;

    public EmployeeAddressRequestValidator()
    {
        RuleFor(x => x.AddressType)
            .IsInEnum().WithMessage("Address type is required.");

        RuleFor(x => x.Country)
            .MaximumLength(LocationMaxLength).WithMessage($"Country must not exceed {LocationMaxLength} characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.Country));

        RuleFor(x => x.State)
            .MaximumLength(LocationMaxLength).WithMessage($"State must not exceed {LocationMaxLength} characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.State));

        RuleFor(x => x.District)
            .MaximumLength(LocationMaxLength).WithMessage($"District must not exceed {LocationMaxLength} characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.District));

        RuleFor(x => x.City)
            .MaximumLength(LocationMaxLength).WithMessage($"City must not exceed {LocationMaxLength} characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.City));

        RuleFor(x => x.ZipCode)
            .MaximumLength(ZipCodeMaxLength).WithMessage($"Zip code must not exceed {ZipCodeMaxLength} characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.ZipCode));

        RuleFor(x => x.AddressLine1)
            .MaximumLength(AddressLineMaxLength).WithMessage($"Address line 1 must not exceed {AddressLineMaxLength} characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.AddressLine1));

        RuleFor(x => x.AddressLine2)
            .MaximumLength(AddressLineMaxLength).WithMessage($"Address line 2 must not exceed {AddressLineMaxLength} characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.AddressLine2));
    }
}
