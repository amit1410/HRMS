using FluentValidation;
using HRMS.Application.DTOs.Designations;
using HRMS.Application.Validators.Common;

namespace HRMS.Application.Validators.Designations;

/// <summary>Shape validation for a designation write; uniqueness is checked in the service.</summary>
public class DesignationRequestValidator : AbstractValidator<DesignationRequest>
{
    public DesignationRequestValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Designation code is required.")
            .MaximumLength(20).WithMessage("Designation code must not exceed 20 characters.")
            .Matches(CodeFormats.Pattern).WithMessage(CodeFormats.Message);

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Designation name is required.")
            .MaximumLength(100).WithMessage("Designation name must not exceed 100 characters.");

        RuleFor(x => x.Description)
            .MaximumLength(500).WithMessage("Description must not exceed 500 characters.");
    }
}
