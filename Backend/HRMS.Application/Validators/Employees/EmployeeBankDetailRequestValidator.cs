using FluentValidation;
using HRMS.Application.DTOs.Employees;
using HRMS.Domain.Enums;

namespace HRMS.Application.Validators.Employees;

/// <summary>
/// Shape and cross-field validation for employee bank detail information. The bank is chosen from the
/// tenant-scoped bank master (never free text); account holder name and account number are required;
/// IFSC code is optional with a basic format check.
/// </summary>
public class EmployeeBankDetailRequestValidator : AbstractValidator<EmployeeBankDetailRequest>
{
    private const int AccountHolderNameMaxLength = 200;
    private const int AccountNumberMaxLength = 30;
    private const int IfscCodeMaxLength = 20;
    private const int BranchNameMaxLength = 200;

    public EmployeeBankDetailRequestValidator()
    {
        RuleFor(x => x.BankId)
            .NotEmpty().WithMessage("Bank is required.");

        RuleFor(x => x.AccountHolderName)
            .NotEmpty().WithMessage("Account holder name is required.")
            .MaximumLength(AccountHolderNameMaxLength).WithMessage($"Account holder name must not exceed {AccountHolderNameMaxLength} characters.");

        RuleFor(x => x.AccountNumber)
            .NotEmpty().WithMessage("Account number is required.")
            .MaximumLength(AccountNumberMaxLength).WithMessage($"Account number must not exceed {AccountNumberMaxLength} characters.");

        RuleFor(x => x.AccountType)
            .IsInEnum().WithMessage("Account type is invalid.");

        RuleFor(x => x.AccountPurpose)
            .IsInEnum().WithMessage("Account purpose is invalid.");

        RuleFor(x => x.Status)
            .IsInEnum().WithMessage("Status is invalid.");

        RuleFor(x => x.IfscCode)
            .MaximumLength(IfscCodeMaxLength).WithMessage($"IFSC code must not exceed {IfscCodeMaxLength} characters.")
            .Matches(@"^[A-Za-z0-9]+$").WithMessage("IFSC code must be alphanumeric.")
            .When(x => !string.IsNullOrWhiteSpace(x.IfscCode));

        RuleFor(x => x.BranchName)
            .MaximumLength(BranchNameMaxLength).WithMessage($"Branch name must not exceed {BranchNameMaxLength} characters.");
    }
}
