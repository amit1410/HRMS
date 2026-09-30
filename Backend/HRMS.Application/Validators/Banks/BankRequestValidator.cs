using FluentValidation;
using HRMS.Application.DTOs.Banks;
using HRMS.Application.Validators.Common;

namespace HRMS.Application.Validators.Banks;

/// <summary>
/// Shape validation for a bank write. Uniqueness of code and name is not checked here: it is a question
/// about stored data within one tenant, so it belongs where the tenant is known — the service.
/// </summary>
public class BankRequestValidator : AbstractValidator<BankRequest>
{
    public BankRequestValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Bank code is required.")
            .MaximumLength(20).WithMessage("Bank code must not exceed 20 characters.")
            .Matches(CodeFormats.Pattern).WithMessage(CodeFormats.Message);

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Bank name is required.")
            .MaximumLength(100).WithMessage("Bank name must not exceed 100 characters.");

        RuleFor(x => x.ShortName)
            .MaximumLength(100).WithMessage("Short name must not exceed 100 characters.");

        RuleFor(x => x.IfscPrefix)
            .MaximumLength(11).WithMessage("IFSC prefix must not exceed 11 characters.")
            .Matches("^[A-Za-z0-9]+$").When(x => !string.IsNullOrWhiteSpace(x.IfscPrefix))
            .WithMessage("IFSC prefix may contain letters and digits only.");

        RuleFor(x => x.BankType)
            .IsInEnum().When(x => x.BankType.HasValue)
            .WithMessage("Bank type is not a recognized value.");

        RuleFor(x => x.Country)
            .MaximumLength(100).WithMessage("Country must not exceed 100 characters.");

        RuleFor(x => x.Remarks)
            .MaximumLength(1000).WithMessage("Remarks must not exceed 1000 characters.");
    }
}
