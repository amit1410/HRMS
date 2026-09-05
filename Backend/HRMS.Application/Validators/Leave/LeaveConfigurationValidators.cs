using FluentValidation;
using HRMS.Application.DTOs.Leave;

namespace HRMS.Application.Validators.Leave;

public sealed class LeaveTypeRequestValidator : AbstractValidator<LeaveTypeRequest>
{
    public LeaveTypeRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(40);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.DefaultUnit).IsInEnum();
    }
}
public sealed class LeavePeriodRequestValidator : AbstractValidator<LeavePeriodRequest>
{
    public LeavePeriodRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(40);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x).Must(x => x.StartDate <= x.EndDate).WithMessage("StartDate must be on or before EndDate.");
    }
}

public sealed class LeavePolicyRequestValidator : AbstractValidator<LeavePolicyRequest>
{
    public LeavePolicyRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(40);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Description).MaximumLength(1000);
    }
}

public sealed class LeavePolicyVersionRequestValidator : AbstractValidator<LeavePolicyVersionRequest>
{
    public LeavePolicyVersionRequestValidator()
    {
        RuleFor(x => x).Must(x => x.EffectiveTo is null || x.EffectiveFrom <= x.EffectiveTo).WithMessage("EffectiveFrom must be on or before EffectiveTo.");
        RuleFor(x => x.Priority).GreaterThanOrEqualTo(0);
    }
}

public sealed class LeavePolicyVersionUpdateRequestValidator : AbstractValidator<LeavePolicyVersionUpdateRequest>
{
    public LeavePolicyVersionUpdateRequestValidator()
    {
        RuleFor(x => x).Must(x => x.EffectiveTo is null || x.EffectiveFrom <= x.EffectiveTo).WithMessage("EffectiveFrom must be on or before EffectiveTo.");
        RuleFor(x => x.Priority).GreaterThanOrEqualTo(0);
    }
}

public sealed class LeaveTypeSelectionRequestValidator : AbstractValidator<LeaveTypeSelectionRequest>
{
    public LeaveTypeSelectionRequestValidator()
    {
        RuleFor(x => x.LeaveTypeIds).Must(ids => ids.Distinct().Count() == ids.Count).WithMessage("LeaveTypeIds must not contain duplicates.");
    }
}
