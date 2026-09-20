namespace HRMS.Application.DTOs.Payroll;

public sealed record PayrollControlConfigurationDto(Guid Id, bool RequireMakerChecker, bool PreventSelfApproval, bool RequireReasonForReopen, bool RequireReasonForCancellation, DateTime UpdatedAtUtc, Guid? UpdatedByUserId);

public sealed class PayrollControlConfigurationRequest
{
    public bool RequireMakerChecker { get; set; } = true;
    public bool PreventSelfApproval { get; set; } = true;
    public bool RequireReasonForReopen { get; set; } = true;
    public bool RequireReasonForCancellation { get; set; } = true;
}

public sealed record PayrollCancellationRequest(string? Reason);
